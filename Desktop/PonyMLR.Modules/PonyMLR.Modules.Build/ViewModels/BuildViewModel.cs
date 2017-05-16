using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
//using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using System.IO;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Data;
using System.Data.Entity;
using System.Xml;
using System.Runtime.Serialization;
using Microsoft.Practices.Prism.ViewModel;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.Prism.Commands;
using PonyMLR.Infrastructure;
using PonyMLR.DataAccess;
using PonyMLR.Logit;

namespace PonyMLR.Modules.Build
{
    public class BuildViewModel : ViewModelBase, IRegionMemberLifetime, IDisposable
    {
        private IRegionManager regionmanager;
        private IEventAggregator eventaggregator;
        private UnitOfWork uow = new UnitOfWork(Globals.DbName.ToLower());
        private StatusSender statusSender;

        //pv calculator bw
        private BackgroundWorker bw_cpv = new BackgroundWorker();

        private bool _showProgressBar = false;

        public BuildViewModel(IRegionManager regionmanager, IEventAggregator eventaggregator)
        {
            this.regionmanager = regionmanager;
            this.eventaggregator = eventaggregator;
            this.statusSender = new StatusSender(this.eventaggregator);

            //default setup values
            this.StartDate = Convert.ToDateTime("01/01/1998");
            this.EndDate = DateTime.Today.AddDays(-1);
            SelectedTrack = "ALL";
            SaveFilePath = @"C:\Users\Lau\Documents\Pony\MLR\Development";
            SaveFileName = @"pv_dev";

            //initialize progress info
            ClearLogText();
            UpdateLogText("Click 'Calculate PVs' to start.");
            UpdateProgressText("calculation not started");
            ProgressValueMax = 100;
            ProgressValue = 0;
            RaisePropertyChangedEvent("ProgressValueMax");
            RaisePropertyChangedEvent("ProgressValue");

            this.CalculatePredictorVariablesCommand = new DelegateCommand<object>(this.CalculatePredictorVariables, this.CanCalculatePredictorVariables);
            this.StopPredictorVariablesCommand = new DelegateCommand<object>(this.StopPredictorVariables, this.CanStopPredictorVariables);

            bw_cpv.WorkerReportsProgress = true;
            bw_cpv.WorkerSupportsCancellation = true;
            bw_cpv.DoWork += new DoWorkEventHandler(bw_cpv_DoWork);
            bw_cpv.ProgressChanged += new ProgressChangedEventHandler(bw_cpv_ProgressChanged);
            bw_cpv.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_cpv_RunWorkerCompleted);

            eventaggregator.GetEvent<DatabaseSetCompletedEvent>().Subscribe(OnDatabaseChanged, ThreadOption.UIThread);
        }

        private void ClearLogText()
        {
            this.LogText = "";
        }

        private void UpdateLogText(string str)
        {
            this.LogText = this.LogText + Environment.NewLine + str;
            RaisePropertyChangedEvent("LogText");
        }

        private void UpdateProgressText(string str)
        {
            this.ProgressText = str;
            RaisePropertyChangedEvent("ProgressText");
        }

        private Dictionary<string, double> PopulatePvDictionary()
        {
            Dictionary<string, double> pvDict = new Dictionary<string, double>();

            foreach (FieldInfo field in typeof(PredictorVariableDefinitions).GetFields().Where(f => (f.Name.StartsWith("PV_") == true && f.Name.StartsWith("PV_MLRS1") == false)))
            {
                    pvDict.Add(field.GetRawConstantValue().ToString(), 0);
            }

            return pvDict;
        }

        private race_card RaceInfoToCard(race_info info)
        {
            race_card card = new race_card();

            //general race info
            card.id = info.race_id;
            card.race_time = info.race_time;
            card.race_name = info.race_name;
            card.race_restrictions_age = info.race_restrictions_age;
            card.race_class = info.race_class;
            card.race_distance = info.race_distance;
            card.race_prize_money = (int)info.race_prize_money;
            card.race_going = info.race_going;
            card.race_number_of_runners = info.race_number_of_runners;
            card.finishing_time = (double)info.race_finishing_time;

            //starters info
            foreach (race_result res in info.race_result)
            {
                starter_info starter = new starter_info();
                starter.horse_name = res.horse_info.horse_name;
                starter.horse_age = (int)res.horse_age;
                starter.stall = (int)res.stall;
                starter.trainer_name = new KeyValuePair<int, string>(res.trainer_info.trainer_id, res.trainer_info.trainer_name);
                starter.jockey_name = new KeyValuePair<int, string>(res.jockey_info.jockey_id, res.jockey_info.jockey_name);
                starter.jockeys_claim = (int)res.jockeys_claim;
                starter.pounds = (int)res.pounds;
                starter.odds = (double)res.odds;
                starter.is_favourite = (bool)res.is_favourite;
                starter.finishing_position = (int)res.finishing_position;
                starter.distance_beaten = (double)res.distance_beaten;
                starter.previousRaces.Clear();
                int hId = res.horse_info.horse_id;

                // year of foal
                int yof = info.race_date.Year - starter.horse_age;
                starter.previousRaces.AddRange(uow.HorseInfoRepository
                    .Get(w => w.horse_id == hId).FirstOrDefault().race_result.ToList()
                    .Where(w=>w.race_info.race_number_of_runners > 1) //ignore walk over race
                    .Where(x => x.race_key < res.race_key)
                    .Where(y => y.race_info.race_date.Year > yof)   //race must be after the year it was foaled
                    .OrderByDescending(z => z.result_id)
                    .Take(Globals.MAX_STARTER_PREVIOUS_RACES));

                card.starters.Add(starter);
            }

            return card;
        }

        #region backgroundWorker
        //pv calculator worker
        private void bw_cpv_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;

            //calculatio start time
            DateTime starttime = DateTime.Now;

            int startRaceId, endRaceId;
            try
            {
                startRaceId = uow.RaceInfoRepository.Get(x => x.race_date == this.StartDate).First().race_id;
                endRaceId = uow.RaceInfoRepository.Get(x => x.race_date == this.EndDate).Last().race_id;
            }
            catch
            {
                statusSender.SendStatus("Start/End Race not found in Database");
                return;
            }
            
            //in case something gone wrong
            if (endRaceId < startRaceId)
                return;

            //create file
            String fileName = SaveFileName + ".csv"; //SelectedTrack + "_" + startRaceId.ToString() + "_" + endRaceId.ToString() + ".csv";
            String folder = SaveFilePath; //"./builder/";
            String path = folder + (folder.EndsWith(@"\") == false? @"\" : "") + fileName;
            try
            {
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
            }
            catch
            {
                statusSender.SendStatus("Invalid Path: " + folder);
                return;
            }

            Dictionary<string, double> pvDict = new Dictionary<string, double>(PopulatePvDictionary());

            //build file header
            string fileHeader = "race_date,race_time,track,race_name,race_restrictions_age,race_class,race_distance,prize_money,going_description," +
                                "number_of_runners,place,distbt,horse_name,stall,trainer,horse_age,jockey_name,jockeys_claim,pounds,odds,fav,comptime,DV_B_WIN,DV_B_PLACE";
            foreach (KeyValuePair<string, double> kvp in pvDict)
            {
                fileHeader = fileHeader + "," + kvp.Key;
            }

            //write header to file
            using (StreamWriter outfile = new StreamWriter(path))
            {
                outfile.WriteLine(fileHeader);
            }

            ProgressValueMax = (endRaceId - startRaceId) + 1;
            ProgressValue = 0;
            RaisePropertyChangedEvent("ProgressValueMax");
            RaisePropertyChangedEvent("ProgressValue");
            UpdateProgressText(@"0/" + (endRaceId - startRaceId + 1).ToString() + " completed");
            for (int raceId = endRaceId; raceId >= startRaceId; raceId--)
            {
                race_info info = uow.RaceInfoRepository.GetByID(raceId);
                string trackName = uow.RaceTrackRepository.Get(t=>t.track_id == info.track_key).FirstOrDefault().track_name;

                // skip if we are not doing for all tracks or it's not the track we are doing
                if ((SelectedTrack != "ALL") && (SelectedTrack != trackName))
                    continue;

                RaceCardModel race_card = new RaceCardModel(info.race_date, new KeyValuePair<int,string>(info.track_key, trackName));
                race_card.races = new List<race_card>();

                //populate pv list with default coeff equals to 0
                race_card.mlrStepOneCoefficients = pvDict;

                //populate race card (assuming one race per meeting)
                race_card.races.Add(RaceInfoToCard(info));

                //update log
                ClearLogText();
                UpdateLogText("Race ID: " + raceId.ToString());
                UpdateLogText("Date: " + race_card.race_date.ToShortDateString());
                UpdateLogText("Track: " + race_card.racetrack.Value.ToString());

                //create calculator
                PredictorVariablesCalc calc = new PredictorVariablesCalc(race_card.race_date, race_card.racetrack, race_card.mlrStepOneCoefficients.Keys.ToList());

                //we've got all the info, calculate pv now
                foreach (race_card rc in race_card.races)
                {
                    //update log                   
                    UpdateLogText("Race Time: " + rc.race_time.ToString());
                    UpdateLogText("Race Name: " + rc.race_name.ToString());
                    UpdateLogText("Runners: " + rc.race_number_of_runners.ToString());

                    statusSender.SendStatus("Calculating predictor variables: " + rc.race_name);
                    rc.has_first_time_out = calc.CalculateAllEntriesPvs(rc);

                    if (rc.has_first_time_out == false)
                    {
                        foreach (starter_info st in rc.starters)
                        {
                            //race and starter values
                            string newLine = race_card.race_date.ToShortDateString() + "," +
                                                rc.race_time.ToString() + "," +
                                                race_card.racetrack.Value.ToString() + "," +
                                                rc.race_name.ToString().Replace(",", "") + "," +
                                                rc.race_restrictions_age.ToString().Replace(",", "") + "," +
                                                rc.race_class.ToString() + "," +
                                                rc.race_distance.ToString() + "," +
                                                rc.race_prize_money.ToString() + "," +
                                                rc.race_going.ToString().Replace(",", "") + "," +
                                                rc.race_number_of_runners.ToString() + "," +
                                                st.finishing_position.ToString() + "," +
                                                st.distance_beaten.ToString() + "," +
                                                st.horse_name.ToString() + "," +
                                                st.stall.ToString() + "," +
                                                st.trainer_name.Value.ToString().Replace(",", "") + "," +
                                                st.horse_age.ToString() + "," +
                                                st.jockey_name.Value.ToString().Replace(",", "") + "," +
                                                st.jockeys_claim.ToString() + "," +
                                                st.pounds.ToString() + "," +
                                                st.odds.ToString() + "," +
                                                st.is_favourite.ToString() + "," +
                                                rc.finishing_time.ToString() + "," +
                                                (Utils.IsWinner(st.finishing_position) == true ? "1" : "0") + "," +
                                                (Utils.IsPlacer(st.finishing_position, rc.race_number_of_runners) == true ? "1" : "0");

                            //pv values
                            foreach (string pv in pvDict.Keys)
                            {
                                newLine = newLine + "," +
                                        st.predictorVariables.Where(x => x.Key == pv).FirstOrDefault().Value.ToString();
                            }

                            using (StreamWriter outfile = new StreamWriter(path, true))
                            {
                                outfile.WriteLine(newLine);
                            }
                        }
                    }

                    //calc.Dispose();
                }

                bw_cpv.ReportProgress(endRaceId - raceId + 1);
                UpdateProgressText((endRaceId - raceId + 1).ToString() + @"/" + (endRaceId - startRaceId + 1).ToString() + " completed");          

                if (bw_cpv.CancellationPending == true)
                {
                    statusSender.SendStatus("Cancelled");
                    return;
                }
            }

            //total time taken
            TimeSpan duration = DateTime.Now - starttime;
            statusSender.SendStatus("Done. Time elapsed: " + duration.ToString(@"dd\.hh\:mm\:ss"));
        }

        private void bw_cpv_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if ((e.Cancelled == true))
            {
                //this.tbProgress.Text = "Canceled!";
            }
            else if (!(e.Error == null))
            {
                //this.tbProgress.Text = ("Error: " + e.Error.Message);
            }
            else
            {
                //this.tbProgress.Text = "Done!";               
            }

        }

        private void bw_cpv_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            this.ProgressValue = e.ProgressPercentage;
            RaisePropertyChangedEvent("ProgressValue");
        }
        #endregion





        private bool CanCalculatePredictorVariables(object arg)
        {
            return true;
        }

        private void CalculatePredictorVariables(object arg)
        {
            if (bw_cpv.IsBusy != true)
            {
                bw_cpv.RunWorkerAsync();
                ClearLogText();
                UpdateLogText("Initializing...");
                statusSender.SendStatus("Starting...");
            }
        }

        private bool CanStopPredictorVariables(object arg)
        {
            return true;
        }

        private void StopPredictorVariables(object arg)
        {
            if (bw_cpv.IsBusy == true)
            {
                bw_cpv.CancelAsync();
            }
        }

        private void OnDatabaseChanged(string module)
        {
            if (Globals.DbName.ToLower().CompareTo(uow.dbName) != 0)
            {
                uow.Dispose();
                uow = new UnitOfWork(Globals.DbName.ToLower());

                RaisePropertyChangedEvent("TrackSelectionsList");
            }
        }

        //progress
        public string LogText { get; private set; }
        public string ProgressText { get; private set; }
        public int ProgressValue { get; private set; }
        public int ProgressValueMax { get; private set; }

        //setup parameters
        public DateTime StartDate { get; private set; }
        public DateTime EndDate { get; private set; }
        public string SelectedTrack { get; private set; }
        public string SaveFilePath { get; private set; }
        public string SaveFileName { get; private set; }
        public List<string> TrackSelectionsList
        {
            get
            {
                List<string> selections = new List<string>();
                if (uow.dbName != "")
                {
                    selections.Add("ALL");
                    selections.AddRange(uow.RaceTrackRepository.Get(x => x.flat_characteristic != "Inactive").Select(y => y.track_name).ToList());
                }
                return selections;
            }
        }


        //commands
        public ICommand CalculatePredictorVariablesCommand { get; private set; }
        public ICommand StopPredictorVariablesCommand { get; private set; }

        public bool KeepAlive
        {
            get { return true; }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~BuildViewModel()   
        {  
            //Finalizer 
            Dispose(false);  
        }
 
        protected virtual void Dispose(bool disposing)  
        {  
            if (disposing)   
            {  
                //free managed resources  
                if (uow != null)  
                {
                    uow.Dispose();
                    uow = null;  
                }

                if (bw_cpv != null)
                {
                    bw_cpv.Dispose();
                    bw_cpv = null;
                }
            }              
        } 
    }
}

