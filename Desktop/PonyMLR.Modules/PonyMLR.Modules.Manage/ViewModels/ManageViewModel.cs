using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
//using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Controls;
using System.Data.Entity;
using Microsoft.Practices.Prism.ViewModel;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.Prism.Commands;
using PonyMLR.Infrastructure;
using PonyMLR.DataAccess;

namespace PonyMLR.Modules.Manage
{
    public class ManageViewModel : ViewModelBase, IRegionMemberLifetime, IDisposable
    {
        private IRegionManager regionmanager;
        private IEventAggregator eventaggregator;
        private UnitOfWork uow = new UnitOfWork(Globals.DbName.ToLower());
        private StatusSender statusSender;

        private BackgroundWorker bw = new BackgroundWorker();

        private ObservableCollection<race_info> _racesInfo;
        private race_info _selectedRace;
        private race_result _selectedStarter;
        private bool _canDisplayRaces;
        private bool _hasNewData = false;

        public ManageViewModel(IRegionManager regionmanager, IEventAggregator eventaggregator)
        {
            this.regionmanager = regionmanager;
            this.eventaggregator = eventaggregator;
            this.statusSender = new StatusSender(this.eventaggregator);

            this.SaveRaceResultsCommand = new DelegateCommand<object>(this.SaveRaceResults, this.CanSaveRaceResults);
            this.SelectionDatesChangedCommand = new DelegateCommand<object>(this.SelectedDatesChanged, this.CanSelectedDatesChanged);
            this.SelectedStarterChangedCommand = new DelegateCommand<object>(this.SelectedStarterChanged, this.CanSelectedStarterChanged);

            bw.WorkerReportsProgress = true;
            bw.WorkerSupportsCancellation = true;
            bw.DoWork += new DoWorkEventHandler(bw_DoWork);
            bw.ProgressChanged += new ProgressChangedEventHandler(bw_ProgressChanged);
            bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_RunWorkerCompleted);

            eventaggregator.GetEvent<DatabaseSetCompletedEvent>().Subscribe(OnDatabaseChanged, ThreadOption.UIThread);

            // discover new data when starts
            DiscoverRaceData();
        }

        public ObservableCollection<race_info> RacesInfo
        {
            get
            {
                if (this._racesInfo == null)
                    return new ObservableCollection<race_info>();
                else
                    return this._racesInfo;
            }
        }

        public race_info SelectedRace
        {
            get
            {
                return this._selectedRace;
            }
            set
            {
                if (this._selectedRace == value) return;
                this._selectedRace = value;
                RaisePropertyChangedEvent("SelectedRace");
            }
        }

        public bool CanDisplayRaces
        {
            get { return this._canDisplayRaces; }
            set
            {
                if (this._canDisplayRaces == value) return;
                this._canDisplayRaces = value;
                RaisePropertyChangedEvent("CanDisplayRaces");
            }
        }

        private void DiscoverRaceData()
        {
            if (bw.IsBusy != true)
            {
                // clear all data before getting new
                if (this._racesInfo != null)
                {
                    this._racesInfo.Clear();
                    RaisePropertyChangedEvent("RacesInfo");
                }
                bw.RunWorkerAsync();
            }
        }

        private void GetRaceDataByDate(DateTime date)
        {
            try
            {
                this._racesInfo = new ObservableCollection<race_info>(uow.RaceInfoRepository.Get(p => p.race_date == date));
                RaisePropertyChangedEvent("RacesInfo");
            }
            catch
            {
                return;
            }
        }

        #region backgroundWorker

        private void bw_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;

            CanDisplayRaces = false;

            //check if latest results exist in database
            DateTime yday = Utils.ConvertTime(DateTime.Now, Globals.GMT_STANDARD_TIME).AddDays(-1).Date;
            List<race_info> info;
            try
            {
                info = new List<race_info>(uow.RaceInfoRepository.Get(p => p.race_date == yday));
            }
            catch (Exception)
            {
                //database error
                CanDisplayRaces = true;
                return;
            }

            if (info.Count() != 0)
            {
                //CanDisplayRaces = true;   
                SelectedDate = yday;            
                e.Result = info;            
            }
            else
            {
                //database not updated. go grab from web
                DateTime latestDt = (DateTime)uow.RaceInfoRepository.Get().Max(x => x.race_date);
                List<DateTime> dateToUpdate = new List<DateTime>();

                while (!latestDt.Date.Equals(yday))
                {
                    latestDt = latestDt.AddDays(1);
                    dateToUpdate.Add(latestDt);
                }

                int rId = uow.RaceInfoRepository.Get().Max(x => x.race_id) + 1;
                foreach (DateTime dt in dateToUpdate)
                {
                    //error handling
                    if (dt.Equals(Utils.ConvertTime(DateTime.Now, Globals.GMT_STANDARD_TIME).Date))
                        break;

                    //clear buffer
                    info.Clear();

                    if ((worker.CancellationPending == true))
                    {
                        e.Cancel = true;
                        break;
                    }
                    else
                    {                     
                        //worker.ReportProgress((i * 10));
                        statusSender.SendStatus("Updating results from web: " + dt.Date.ToShortDateString());

                        WebDataUpdater updater = new WebDataUpdater(dt, uow.RaceTrackRepository.Get(x=>x.flat_characteristic.CompareTo("Inactive") != 0).ToDictionary(t => t.track_id, t => t.track_name), uow,
                                                                uow.TrainerInfoRepository.Get().ToDictionary(t => t.trainer_id, t=> new Tuple<String, String>(t.trainer_name, t.alternate_name)),
                                                                uow.JockeyInfoRepository.Get().ToDictionary(j => j.jockey_id, j=> new Tuple<String, String>(j.jockey_name, j.alternate_name)));
                        foreach (Uri link in updater.GetResultsUrl())
                        {
                            // Perform a time consuming operation and report progress.
                            System.Threading.Thread.Sleep(50);

                            race_info raceInfo = updater.GetRaceInfo(link, rId);
                            List<race_result> raceResults = new List<race_result>();
                            raceResults = updater.GetRaceResults(link, rId);
                            foreach (race_result r in raceResults)
                            {
                                raceInfo.race_result.Add(r);
                            }
                        
                            //if stall equals zero exists, that could be a jump race, skip the whole race
                            if (raceInfo.race_result.Any(x=>x.stall <= 0) == true)
                                continue;

                            if (raceInfo.race_result.Count() != 0)
                            {
                                info.Add(raceInfo);
                                rId++;
                            }
                        }

                        if (info.Count != 0)
                        {
                            // insert into database only after all results are collected
                            foreach (race_info ri in info)
                            {
                                uow.RaceInfoRepository.Insert(ri);
                            }                        
                            _hasNewData = true;
                            CommandStateChanged();
                        }
                    }
                }

                SelectedDate = latestDt;
                e.Result = info;           
            }
            RaisePropertyChangedEvent("SelectedDate");
        }

        private void bw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if ((e.Cancelled == true))
            {
                //this.tbProgress.Text = "Canceled!";
                CanDisplayRaces = false;
            }
            else if (!(e.Error == null))
            {
                //this.tbProgress.Text = ("Error: " + e.Error.Message);
                CanDisplayRaces = false;
            }
            else
            {
                //this.tbProgress.Text = "Done!";
                if (e.Result != null)
                {                   
                    statusSender.SendStatus("Result updated");
                    this._racesInfo = new ObservableCollection<race_info>((List<race_info>)e.Result);
                    CanDisplayRaces = true;
                    RaisePropertyChangedEvent("RacesInfo");                 
                }              
            }          
        }

        private void bw_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            //this.tbProgress.Text = (e.ProgressPercentage.ToString() + "%");
        }

        #endregion

        private void CommandStateChanged()
        {
            ((DelegateCommand<object>)SaveRaceResultsCommand).RaiseCanExecuteChanged();
        }

        private void SaveRaceResults(object arg)
        {
            //save all races
            try
            {
                uow.Save();

                statusSender.SendStatus("Result saved");
                _hasNewData = false;
                CommandStateChanged();
            }
            catch (Exception e)
            {
                statusSender.SendStatus("Save Result Failed: " + e.Message);
            }
        }
               
        private bool CanSaveRaceResults(object arg)
        {
            return _hasNewData;
        }

        public void SelectedDatesChanged(object sender)
        {
            GetRaceDataByDate(SelectedDate);           
            CanDisplayRaces = true;
        }

        private void SelectedStarterChanged(object sender)
        {
            if (SelectedStarter != null)
            {
                eventaggregator.GetEvent<ResearchPanelUpdateEvent>()
                    .Publish(uow.RaceResultRepository.Get(x => x.horse_key.CompareTo(SelectedStarter.horse_key) == 0).OrderByDescending(y => y.result_id).Take(Globals.MAX_STARTER_PREVIOUS_RACES).ToList());
            }
        }

        private bool CanSelectedDatesChanged(object arg) { return true; }
        private bool CanSelectedStarterChanged(object arg) { return true; }

        public DateTime SelectedDate { get; private set; }

        public race_result SelectedStarter
        {
            get
            {
                return this._selectedStarter;
            }
            set
            {
                if (this._selectedStarter == value) return;
                this._selectedStarter = value;
                RaisePropertyChangedEvent("SelectedStarter");
            }
        }

        private void OnDatabaseChanged(string module)
        {
            if ((Globals.DbName.ToLower().CompareTo(uow.dbName) != 0) && (bw.IsBusy != true))
            {
                uow.Dispose();
                uow = new UnitOfWork(Globals.DbName.ToLower());

                DiscoverRaceData();
            }
        }

        public ICommand SaveRaceResultsCommand { get; private set; }
        public ICommand SelectionDatesChangedCommand { get; private set; }
        public ICommand SelectedStarterChangedCommand { get; private set; }

        public bool KeepAlive
        {
            get { return true; }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ManageViewModel()   
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

                if (bw != null)
                {
                    bw.Dispose();
                    bw = null;
                }
            }            
        } 
    }
}