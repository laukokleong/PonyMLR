using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
//using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

namespace PonyMLR.Modules.Calculate
{
    public class CalculateViewModel : ViewModelBase, IRegionMemberLifetime, IDisposable
    {
        private IRegionManager regionmanager;
        private IEventAggregator eventaggregator;
        private UnitOfWork uow = new UnitOfWork(Globals.DbName.ToLower());
        private StatusSender statusSender;

        //race cards finder bw
        private BackgroundWorker bw_rcf = new BackgroundWorker();
        //pv calculator bw
        private BackgroundWorker bw_cpv = new BackgroundWorker();
        //logit bw
        private BackgroundWorker bw_mlr = new BackgroundWorker();

        private ObservableCollection<RaceCardModel> _racecards;
        private race_card _selectedCard;
        private starter_info _selectedStarter;
        private bool _canDisplayRaceCard;
        private bool _allowRecalculatePv;

        // calculation options parameters
        private ObservableCollection<string> _calculationOptions;
        private string _selectedCalcOption;

        // MLR version selection
        private string _selectedMlrVersion;

        //settings parameters
        private bool _autorefresh;
        private int _refreshinterval;
        private int _oddsdeviationtrigger = 5;
        private int _minimumbackodds = 2;
        private int _maximumbackodds = 26;
        private int _minimumlayodds = 1;
        private int _maximumlayodds = 5;

        public CalculateViewModel(IRegionManager regionmanager, IEventAggregator eventaggregator)
        {
            this.regionmanager = regionmanager;
            this.eventaggregator = eventaggregator;
            this.statusSender = new StatusSender(this.eventaggregator);

            this._racecards = new ObservableCollection<RaceCardModel>();
            CanDisplayRaceCard = true;

            SelectedDate = DateTime.Now;

            this.FindRaceCardCommand = new DelegateCommand<object>(this.FindRaceCard, this.CanFindRaceCard);
            this.SelectionDatesChangedCommand = new DelegateCommand<object>(this.SelectedDatesChanged, this.CanSelectedDatesChanged);
            this.SelectedStarterChangedCommand = new DelegateCommand<object>(this.SelectedStarterChanged, this.CanSelectedStarterChanged);
            this.CalculatePredictorVariablesCommand = new DelegateCommand<object>(this.CalculatePredictorVariables, this.CanCalculatePredictorVariables);
            this.RunMultinomialLogitCommand = new DelegateCommand<object>(this.RunMultinomialLogit, this.CanRunMultinomialLogit);
            this.StopMultinomialLogitCommand = new DelegateCommand<object>(this.StopMultinomialLogit, this.CanStopMultinomialLogit);
            this.SaveCalculationCommand = new DelegateCommand<object>(this.SaveCalculation, this.CanSaveCalculation);
            this.RemoveStarterFromRaceCommand = new DelegateCommand<object>(this.RemoveStarterFromRace, this.CanRemoveStarterFromRace);
            this.OddsSettingsChangedCommand = new DelegateCommand<object>(this.OddsSettingsChanged, this.CanOddsSettingsChanged);

            bw_rcf.WorkerReportsProgress = true;
            bw_rcf.WorkerSupportsCancellation = true;
            bw_rcf.DoWork += new DoWorkEventHandler(bw_rcf_DoWork);
            bw_rcf.ProgressChanged += new ProgressChangedEventHandler(bw_rcf_ProgressChanged);
            bw_rcf.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_rcf_RunWorkerCompleted);

            bw_cpv.WorkerReportsProgress = true;
            bw_cpv.WorkerSupportsCancellation = true;
            bw_cpv.DoWork += new DoWorkEventHandler(bw_cpv_DoWork);
            bw_cpv.ProgressChanged += new ProgressChangedEventHandler(bw_cpv_ProgressChanged);
            bw_cpv.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_cpv_RunWorkerCompleted);

            bw_mlr.WorkerReportsProgress = true;
            bw_mlr.WorkerSupportsCancellation = true;
            bw_mlr.DoWork += new DoWorkEventHandler(bw_mlr_DoWork);
            bw_mlr.ProgressChanged += new ProgressChangedEventHandler(bw_mlr_ProgressChanged);
            bw_mlr.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_mlr_RunWorkerCompleted);

            RaceCards = new ListCollectionView(_racecards);
            RaceCards.CurrentChanged += new EventHandler(RaceCards_CurrentChanged);
            RaceCards.CurrentChanging += new CurrentChangingEventHandler(RaceCards_CurrentChanging);

            this._calculationOptions = new ObservableCollection<string>();
            this._calculationOptions.Add(Globals.CALCULATE_PV_THIS_RACE_ONLY);
            this._calculationOptions.Add(Globals.CALCULATE_PV_CURRENT_MEETING);
            this._calculationOptions.Add(Globals.CALCULATE_PV_ALL_MEETINGS);
            SelectedCalcOption = CalculationOptions[2];

            SelectedMlrVersion = MlrVersions.FirstOrDefault();

            eventaggregator.GetEvent<DatabaseSetCompletedEvent>().Subscribe(OnDatabaseChanged, ThreadOption.UIThread);
        }

        private void FindRaceCard(object arg)
        {
            if (bw_rcf.IsBusy != true)
                bw_rcf.RunWorkerAsync();
        }

        private void CalculatePredictorVariables(object arg)
        {
            if ((bw_cpv.IsBusy != true) && (bw_rcf.IsBusy != true))
            {
                if ((IsPvCalculated() == false) || (this._allowRecalculatePv == true))
                {
                    this._allowRecalculatePv = false;
                    bw_cpv.RunWorkerAsync();
                }
            }
        }

        private void RunMultinomialLogit(object arg)
        {
            if (bw_cpv.IsBusy == true)
                return;

            //maybe we need to calculate pv
            CalculatePredictorVariables(null);

            if (bw_mlr.IsBusy != true)
                bw_mlr.RunWorkerAsync();            
        }

        private void StopMultinomialLogit(object arg)
        {
            if (bw_mlr.IsBusy == true)
                bw_mlr.CancelAsync();
        }

        private void SaveCalculation(object arg)
        {
            RaceCardModel rc = (RaceCardModel)RaceCards.CurrentItem;
            StringBuilder sb = new StringBuilder();

            // first line is header
            sb.AppendLine("STALL,HORSE_NAME,AGE,WEIGHT,EARLY_SPEED,JOCKEY_NAME,TRAINER_NAME,PUBLIC,FUNDAMENTAL,FAIR_ODDS,ACTUAL_ODDS,DEVIATION");
            foreach(race_card race in rc.races)
            {
                sb.AppendLine(race.race_time.ToString());
                foreach(starter_info starter in race.starters)
                {
                    string csv_line = starter.stall.ToString() + "," +
                                        starter.horse_name + "," +
                                        starter.horse_age.ToString() + "," +
                                        starter.pounds.ToString() + "," +
                                        starter.early_speed.ToString() + "," +
                                        starter.jockey_name.Value.Replace(',', ' ') + "," +
                                        starter.trainer_name.Value.Replace(',', ' ') + "," +
                                        starter.mlr1.probability_public.ToString() + "," +
                                        starter.mlr1.probability_fundamental.ToString() + "," +
                                        starter.mlr2.odds_fair.ToString() + "," +
                                        starter.mlr2.odds_actual.ToString() + "," +
                                        starter.mlr2.odds_deviation.ToString();

                    sb.AppendLine(csv_line);
                }
            }

            String fileName = rc.racetrack.Value + "_" + rc.race_date.Day.ToString() + rc.race_date.Month.ToString() + rc.race_date.Year.ToString() + ".csv";
            String folder = Utils.GetMyDocumentsFolderPath() + Globals.FOLDER_MLR_CALCULATIONS;
            String path = folder + fileName;
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            using (StreamWriter outfile = new StreamWriter(path))
            {
                outfile.Write(sb.ToString());
            }
        }

        private void RemoveStarterFromRace(object arg)
        {
            if (this._selectedStarter != null)
            {
                this._selectedCard.starters.Remove(this._selectedStarter);
                this._selectedCard.race_number_of_runners = this._selectedCard.starters.Count;
                this._allowRecalculatePv = true;
                RefreshStartersTable();
            }
        }

        private List<String> GetRaceCardFilePath(DateTime dt)
        {                       
            List<String> rc = new List<String>();

            //try look for it locally first
            try
            {
                string[] allFiles = Directory.GetFiles(Utils.GetMyDocumentsFolderPath() + Globals.FOLDER_RACECARD, "*.html");
                if (allFiles.Count() != 0)
                {
                    foreach (string rcfile in allFiles)
                    {
                        string dateStr = Regex.Match(Path.GetFileName(rcfile), "([0-3][0-9])(-)([0-1][0-9])(-)([0-9]*)").ToString();
                        if (Convert.ToDateTime(dateStr).Date.Equals(dt.Date) == true)
                            rc.Add(rcfile);
                    }
                }
            }
            catch (Exception)
            {
                // open file error, assume no racecard exist, continue to get racecards from web
            }

            if (rc.Count != 0)
                return rc;

            //nothing found locally, now try to get it from the web
            try
            {
                statusSender.SendStatus("Downloading Racecards from web");
                RaceCardLookup lookup = new RaceCardLookup(dt, uow.RaceTrackRepository.Get(x => x.flat_characteristic.CompareTo("Inactive") != 0).ToDictionary(t => t.track_id, t => t.track_name));
                foreach (List<Uri> raceList in lookup.GetRaceCardsUrl())
                {
                    rc.Add(lookup.SaveSingleMeetingAsHtml(raceList));
                }
            }
            catch (Exception e)
            {
                statusSender.SendStatus(e.Message);
                statusSender.SuppressFutureMessage(true);
            }

            return rc;
        }

        private double AverageEarlySpeedFigure(List<race_result> praces, int num_of_races)
        {
            if (praces.Count() == 0)
                return Globals.EARLY_SPEED_FIGURE_NOT_RATED;

            double esftotal = 0;
            int rcount = 0;
            foreach (race_result race in praces)
            {
                double esf = Utils.CommentToEarlySpeedFigure(race.race_comment);

                if (esf != Globals.EARLY_SPEED_FIGURE_UNKNOWN)
                {
                    esftotal = esftotal + esf;
                    rcount++;
                    if (rcount >= num_of_races)
                        break;
                }
            }

            if (rcount != 0)
                return (esftotal / rcount);

            return Globals.EARLY_SPEED_FIGURE_NOT_RATED;
        }

        private List<RaceCardModel> PopulateDataToRaceCards(List<String> files)
        {
            List<RaceCardModel> ret = new List<RaceCardModel>();

            if (files.Count == 0)
                return ret;

            Dictionary<int, String> trackDict;
            try { trackDict = uow.RaceTrackRepository.Get(x => x.flat_characteristic.CompareTo("Inactive") != 0).ToDictionary(t => t.track_id, t => t.track_name); }
            catch { return null; }
            RaceCardLookup lookup = new RaceCardLookup(null, trackDict,
                                                            uow.TrainerInfoRepository.Get().ToDictionary(t => t.trainer_id, t=> new Tuple<String, String>(t.trainer_name, t.alternate_name)),
                                                            uow.JockeyInfoRepository.Get().ToDictionary(j => j.jockey_id, j => new Tuple<String, String>(j.jockey_name, j.alternate_name)));
          
            foreach (String rc in files)
            {
                string dateStr = Regex.Match(Path.GetFileName(rc), "([0-3][0-9])(-)([0-1][0-9])(-)([0-9]*)").ToString();
                DateTime dt;
                DateTime.TryParse(dateStr, out dt);

                KeyValuePair<int, String> trackPair;
                if (rc.Contains("newmarket") == true)
                    trackPair = trackDict.FirstOrDefault(x => x.Key == Utils.GetNewmarketCourseKey(dt));
                else
                    trackPair = trackDict.FirstOrDefault(x => rc.Contains(Utils.RemoveStringInBracket(x.Value).ToLower()));

                statusSender.SendStatus("Populating racecard data: " + trackPair.Value);
                RaceCardModel card = new RaceCardModel(dt, trackPair);
                card.races = lookup.GetRaceCards(rc);

                foreach (race_card race in card.races)
                {
                    foreach (starter_info starter in race.starters)
                    {
                        starter.previousRaces.Clear();

                        // get horse id
                        int hId = 0;
                        try
                        {
                            string brname = Utils.RemoveStringInBracket(starter.horse_name);
                            List<horse_info> hil = new List<horse_info>(uow.HorseInfoRepository.Get(x => x.horse_name.ToLower().Replace("'", "").Contains(brname.ToLower().Replace("'", "")) == true)
                                                                            .Where(y => Utils.RemoveStringInBracket(y.horse_name.ToLower().Replace("'", "")).Equals(brname.ToLower().Replace("'", "")) == true)
                                                                            );
                            if (hil.Count() > 1)
                            {
                                string bracketedstr = Utils.GetStringInBracket(starter.horse_name);
                                foreach (horse_info hi in hil)
                                {
                                    if (bracketedstr == Utils.GetStringInBracket(hi.horse_name))
                                        hId = hi.horse_id;
                                }
                            }
                            else
                            {
                                hId = hil.LastOrDefault().horse_id;
                            }

                        }
                        catch (Exception) { }


                        if (hId != 0)
                        {
                            // year of foal
                            int yof = dt.Year - starter.horse_age;
                            starter.previousRaces.AddRange(uow.HorseInfoRepository
                                .Get(w => w.horse_id == hId).FirstOrDefault().race_result
                                .Where(x=>x.race_info.race_number_of_runners > 1) //ignore walk over race
                                .Where(y => y.race_info.race_date.Year > yof)   //race must be after the year it was foaled
                                .OrderByDescending(z => z.result_id)
                                .Take(Globals.MAX_STARTER_PREVIOUS_RACES));

                            starter.early_speed = AverageEarlySpeedFigure(starter.previousRaces, 3);
                        }
                        else
                        {
                            starter.early_speed = Globals.EARLY_SPEED_FIGURE_NOT_RATED;
                        }
                    }
                }

                //populate MLR Coefficients
                string track = Utils.RemoveStringInBracket(card.racetrack.Value);

                try
                {
                    //special case for Lingfield              
                    if (track.ToLower().CompareTo("lingfield") == 0)
                    {   
                        // separate lingfield into 2 different cards (aw and turf) if needed
                        RaceCardModel card_aw = new RaceCardModel(dt, new KeyValuePair<int, String>(trackPair.Key, trackPair.Value + " (AW)"));
                        card_aw.races = new List<race_card>();
                        RaceCardModel card_turf = new RaceCardModel(dt, new KeyValuePair<int, String>(trackPair.Key, trackPair.Value + " (Turf)"));
                        card_turf.races = new List<race_card>();
                        foreach (race_card race in card.races)
                        {
                            string going = race.race_going;
                            if (going.CompareTo(Globals.AW_GOING_STANDARD) == 0 ||
                                going.CompareTo(Globals.AW_GOING_FAST) == 0 || going.CompareTo(Globals.AW_GOING_STANDARD_TO_FAST) == 0 ||
                                going.CompareTo(Globals.AW_GOING_STANDARD_TO_SLOW) == 0 || going.CompareTo(Globals.AW_GOING_SLOW) == 0)
                                card_aw.races.Add(race);
                            else
                                card_turf.races.Add(race);
                        }

                        if(card_aw.races.Count() != 0)
                        {
                            track = track + "_AWT";
                            card_aw.mlrStepOneCoefficients = GetMlrCoefficients(track + "_STEP1");
                            card_aw.mlrStepTwoCoefficients = GetMlrCoefficients(track + "_STEP2");
                            if (card_aw.mlrStepTwoCoefficients.Count() == 0)
                                card_aw.dependentVarStdDeviation = GetMlrDvStdDeviation(track + "_STEP1");
                            else
                                card_aw.dependentVarStdDeviation = GetMlrDvStdDeviation(track + "_STEP2");

                            ret.Add(card_aw);
                        }
                        if (card_turf.races.Count() != 0)
                        {
                            track = track + "_TURF";
                            card_turf.mlrStepOneCoefficients = GetMlrCoefficients(track + "_STEP1");
                            card_turf.mlrStepTwoCoefficients = GetMlrCoefficients(track + "_STEP2");
                            if (card_turf.mlrStepTwoCoefficients.Count() == 0)
                                card_turf.dependentVarStdDeviation = GetMlrDvStdDeviation(track + "_STEP1");
                            else
                                card_turf.dependentVarStdDeviation = GetMlrDvStdDeviation(track + "_STEP2");

                            ret.Add(card_turf);
                        }

                        continue;
                    }

                    //special case for Newmarket
                    if (track.ToLower().CompareTo("newmarket") == 0)
                    {
                        int month = dt.Month;
                        if (month == 6 || month == 7 || month == 8)
                            track = track + "_JULY";
                        else
                            track = track + "_ROWLEY";
                    }
                }
                catch (Exception) { }

                card.mlrStepOneCoefficients = GetMlrCoefficients(track + "_STEP1");
                card.mlrStepTwoCoefficients = GetMlrCoefficients(track + "_STEP2");
                if (card.mlrStepTwoCoefficients.Count() == 0)
                    card.dependentVarStdDeviation = GetMlrDvStdDeviation(track + "_STEP1");
                else
                    card.dependentVarStdDeviation = GetMlrDvStdDeviation(track + "_STEP2");

                if (card.races.Count != 0)
                    ret.Add(card);
            }

            return ret;
        }

        private Dictionary<string, double>  GetMlrCoefficients(String racetrack)
        {
            Dictionary<string, double> ret = new Dictionary<string, double>();

            //look for coefficients files
            try
            {
                string directory = Globals.FOLDER_MLR_COEFFICIENTS + SelectedMlrVersion;
                string file = Directory.GetFiles(directory, "MLREST_" + racetrack.ToUpper() + "_*.xml").FirstOrDefault();
                if (file.CompareTo("") != 0)
                {
                    MlrCoefficientsLoader loader = new MlrCoefficientsLoader(file);

                    //verify that the content we are getting is correct
                    if (!loader.VerifyFile(racetrack))
                        return ret;

                    ret = loader.GetAllCoefficients();
                }
            }
            catch (Exception)
            {
                return ret;
            }

            return ret;
        }

        private double GetMlrDvStdDeviation(String racetrack)
        {
            double ret = 0;

            //look for standard deviation in coefficients files
            try
            {
                string directory = Globals.FOLDER_MLR_COEFFICIENTS + SelectedMlrVersion;
                string file = Directory.GetFiles(directory, "MLREST_" + racetrack.ToUpper() + "_*.xml").FirstOrDefault();
                if (file.CompareTo("") != 0)
                {
                    MlrCoefficientsLoader loader = new MlrCoefficientsLoader(file);

                    //verify that the content we are getting is correct
                    if (!loader.VerifyFile(racetrack))
                        return ret;

                    ret = loader.GetDvStandardDeviation();
                }
            }
            catch (Exception)
            {
                return ret;
            }

            return ret;
        }

        private void CalculateSingleMeetingPvs(RaceCardModel racecards)
        {
            if (racecards == null)
                return;

            //list of MLR Step 1 PVs to calculate
            List<string> pvList = new List<string>(racecards.mlrStepOneCoefficients.Keys);
            PredictorVariablesCalc calc = new PredictorVariablesCalc(racecards.race_date, racecards.racetrack, pvList);

            if (SelectedCalcOption.CompareTo(Globals.CALCULATE_PV_THIS_RACE_ONLY) == 0)
            {
                if (SelectedCard != null)
                {
                    statusSender.SendStatus("Calculating predictor variables: " + SelectedCard.race_name);
                    SelectedCard.has_first_time_out = calc.CalculateAllEntriesPvs(SelectedCard);
                }
            }
            else
            {
                foreach (race_card rc in racecards.races)
                {
                    statusSender.SendStatus("Calculating predictor variables: " + rc.race_name);
                    rc.has_first_time_out = calc.CalculateAllEntriesPvs(rc);
                }
            }

            calc.Dispose();
        }

        private bool IsPvCalculated()
        {
            bool ret = true;

            if (SelectedCalcOption.CompareTo(Globals.CALCULATE_PV_ALL_MEETINGS) == 0)
            {
                foreach (RaceCardModel rc in RaceCards)
                {
                    if (rc.races.Where(w => w.has_first_time_out == false).Any(x => x.starters.Any(y => y.predictorVariables.Count == 0)) == true)
                    {
                        ret = false;
                        break;
                    }
                }
            }
            else if (SelectedCalcOption.CompareTo(Globals.CALCULATE_PV_CURRENT_MEETING) == 0)
            {
                RaceCardModel current = (RaceCardModel)RaceCards.CurrentItem;
                if (current.races.Where(w => w.has_first_time_out == false).Any(x => x.starters.Any(y => y.predictorVariables.Count == 0)) == true)
                    ret = false;
            }
            else if (SelectedCalcOption.CompareTo(Globals.CALCULATE_PV_THIS_RACE_ONLY) == 0)
            {
                if (SelectedCard.starters.Any(x=>x.predictorVariables.Count == 0) == true)
                    ret = false;
            }


            return ret;
        }

        private bool IsMlrCalculated(RaceCardModel rc)
        {
            if (rc == null)
                return false;

            bool ret = true;
            if (rc.races.Where(w => w.has_first_time_out == false).Any(x => x.starters.Any(y => y.mlr1.probability_fundamental == 0)) == true)
                ret = false;

            return ret;
        }

        private void RefreshStartersTable()
        {
            // dummy settings just to refresh the calculation results and odds
            race_card rc = this._selectedCard;
            SelectedCard = null;
            SelectedCard = rc;
        }

        private void RunSigleMeetingMultinomialLogit(RaceCardModel racecards)
        {
            if (racecards == null)
                return;

            statusSender.SendStatus("Calculating...");

            OddsUpdater updater = new OddsUpdater(racecards.racetrack.Value, racecards.race_date);

            foreach (race_card rc in racecards.races)
            {
                // update odds of future races
                if (TimeSpan.Compare(rc.race_time, Utils.ConvertTime(DateTime.Now, Globals.GMT_STANDARD_TIME).TimeOfDay) == 1)
                {
                    //string time_str = (rc.race_time.Hours % Globals.ROUND_THE_CLOCK_HOURS).ToString() + ":" + rc.race_time.Minutes.ToString("00");
                    string time_str = (rc.race_time.Hours).ToString("00") + ":" + rc.race_time.Minutes.ToString("00");
                    string oddsDoc = updater.GetOddsDoc<string>(time_str);
                    foreach (starter_info starter in rc.starters)
                    {
                        double previous_odds = starter.odds;
                        starter.odds = updater.UpdateOdds(oddsDoc, starter.horse_name);
                        if ((starter.odds != Globals.UNDEFINED_ODDS) && (previous_odds != Globals.UNDEFINED_ODDS) && (previous_odds != 0))
                        {
                            if (previous_odds > starter.odds)
                                starter.oddsMovement = OddsMovement.SHORTENING;
                            else if (previous_odds < starter.odds)
                                starter.oddsMovement = OddsMovement.DRIFTING;
                            else
                                starter.oddsMovement = OddsMovement.UNCHANGED;
                        }
                    }
                }
                if (rc.has_first_time_out == true)
                    continue;

                //list of MLR Step 2 PVs to calculate
                LogitCalc calc = new LogitCalc(racecards.mlrStepOneCoefficients, racecards.mlrStepTwoCoefficients);
             
                calc.CalculateAllEntriesPrediction(rc);
                SetBetVerdict(rc);
                //SetKellyPercentage(rc);
                SolveKellyFraction(rc);
            }
        }

        private void SetBetVerdict(race_card racecard)
        {
            foreach (starter_info starter in racecard.starters)
            {
                // back
                if (starter.mlr2.odds_deviation > 0)
                {
                    if (starter.mlr2.odds_actual >= MinimumBackOdds && starter.mlr2.odds_actual <= MaximumBackOdds)
                    {                     
                        if (Math.Abs(starter.mlr2.odds_deviation) > ((double)OddsDeviationTrigger / Globals.PERCENTAGE_DIVIDER))
                        {
                            starter.verdict = BetVerdict.BACK;
                            continue;
                        }
                    }
                }
                // lay
                else
                {
                    if (starter.mlr2.odds_actual >= MinimumLayOdds && starter.mlr2.odds_actual <= MaximumLayOdds)
                    {
                        if (Math.Abs(starter.mlr2.odds_deviation) > ((double)OddsDeviationTrigger / Globals.PERCENTAGE_DIVIDER))
                        {
                            starter.verdict = BetVerdict.LAY;
                            continue;
                        }
                    }
                }

                starter.verdict = BetVerdict.NO_BET;
            }
        }

        private void SetKellyPercentage(race_card racecard)
        {
            KellyCalculator calc = new KellyCalculator();

            foreach (starter_info starter in racecard.starters)
            {
                starter.kellyPercentage = calc.GetKellyFraction(starter.mlr1.odds_public, starter.mlr2.probability_fair);
            }
        }

        private void SolveKellyFraction(race_card racecard)
        {
            List<KellyParam> param = new List<KellyParam>();

            foreach (starter_info starter in racecard.starters)
            {
                KellyParam kp = new KellyParam();
                kp.Id = starter.stall;
                kp.Name = starter.horse_name;
                kp.FairProbability = starter.mlr2.probability_fair;
                kp.ActualOdds = starter.mlr2.odds_actual;

                param.Add(kp);
            }

            // solve Kelly
            KellyCalculator calc = new KellyCalculator(param.ToArray());
            calc.SolveKellyCriterion();

            // write result to race card
            foreach (starter_info starter in racecard.starters)
            {
                starter.kellyPercentage = param.Where(x => x.Name == starter.horse_name).FirstOrDefault().Fraction;
            }
        }

        #region backgroundWorker
        //race card finder worker
        private void bw_rcf_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;

            DateTime dt = Utils.ConvertTime(DateTime.Now, Globals.GMT_STANDARD_TIME);
            if (e.Argument != null)
                dt = (DateTime)e.Argument;

            CanDisplayRaceCard = false;
            e.Result = PopulateDataToRaceCards(GetRaceCardFilePath(dt));
        }

        private void bw_rcf_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
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
                if (e.Result != null)
                {
                    this._racecards.Clear();
                    foreach (RaceCardModel rc in (List<RaceCardModel>)e.Result)
                        this._racecards.Add(rc);

                    if (RaceCards.CurrentItem == null)
                        RaceCards.MoveCurrentToFirst();

                    statusSender.SendStatus("Done");
                    RaisePropertyChangedEvent("RaceCards");                  
                    CommandStateChanged();
                }                      
            }

            CanDisplayRaceCard = true;
        }

        private void bw_rcf_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            //this.tbProgress.Text = (e.ProgressPercentage.ToString() + "%");
        }

        //pv calculator worker
        private void bw_cpv_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;

            CanDisplayRaceCard = false;
            if (SelectedCalcOption.CompareTo(Globals.CALCULATE_PV_ALL_MEETINGS) == 0)
            {
                foreach (RaceCardModel rc in RaceCards)
                    CalculateSingleMeetingPvs(rc);
            }
            else
            {
                CalculateSingleMeetingPvs((RaceCardModel)RaceCards.CurrentItem);
            }
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
                statusSender.SendStatus("Done");
                RaisePropertyChangedEvent("RaceCards");
                CommandStateChanged();
            }

            CanDisplayRaceCard = true;
        }

        private void bw_cpv_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            //this.tbProgress.Text = (e.ProgressPercentage.ToString() + "%");
        }

        //mlr worker
        private void bw_mlr_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;

            CommandStateChanged();

            //if PV calculation is running, wait
            while (bw_cpv.IsBusy == true) { System.Threading.Thread.Sleep(500); }

            statusSender.SendStatus("MLR started");
            int counter = 0;
            while (true)
            {
                if ((worker.CancellationPending == true))
                {
                    e.Cancel = true;
                    break;
                }
                else if (counter <= 0)
                {
                    RunSigleMeetingMultinomialLogit((RaceCardModel)RaceCards.CurrentItem);
                    worker.ReportProgress(0, (RaceCardModel)RaceCards.CurrentItem);
                    statusSender.SendStatus("Last Updated at " + DateTime.Now.ToShortTimeString());

                    //reset down counter
                    counter = (RefreshInterval * Globals.SECONDS_PER_MINUTE);
                }

                if ((AutoRefresh == false) || (RefreshInterval == 0))
                    break;

                // sleep 1 second
                System.Threading.Thread.Sleep(Globals.MILISECONDS_MULTIPLIER);
                counter--;
            }
        }

        private void bw_mlr_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if ((e.Cancelled == true))
            {
                //this.tbProgress.Text = "Canceled!";
                RaisePropertyChangedEvent("RaceCards");
                CommandStateChanged();
            }
            else if (!(e.Error == null))
            {
                //this.tbProgress.Text = ("Error: " + e.Error.Message);
            }
            else
            {
                //this.tbProgress.Text = "Done!";
                statusSender.SendStatus("MLR stopped");
                CommandStateChanged();
            }
        }

        private void bw_mlr_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            RaisePropertyChangedEvent("RaceCards");
            RefreshStartersTable();          
        }
        #endregion

        private void CommandStateChanged()
        {
            ((DelegateCommand<object>)FindRaceCardCommand).RaiseCanExecuteChanged();
            ((DelegateCommand<object>)CalculatePredictorVariablesCommand).RaiseCanExecuteChanged();
            ((DelegateCommand<object>)RunMultinomialLogitCommand).RaiseCanExecuteChanged();
            ((DelegateCommand<object>)StopMultinomialLogitCommand).RaiseCanExecuteChanged();
            ((DelegateCommand<object>)SaveCalculationCommand).RaiseCanExecuteChanged();
        }

        public bool CanDisplayRaceCard
        {
            get { return this._canDisplayRaceCard; }
            set
            {
                if (this._canDisplayRaceCard != value)
                    this._canDisplayRaceCard = value;
                RaisePropertyChangedEvent("CanDisplayRaceCard");
            }
        }

        private bool CanFindRaceCard(object arg)
        {
            bool ret = (this._racecards.Count() == 0 ? true : false);
            return ret;
        }

        private bool CanCalculatePredictorVariables(object arg)
        {
            bool ret = (RaceCards.CurrentItem == null ? false : true);
            return ret;
        }

        private bool CanRunMultinomialLogit(object arg)
        {
            return ((!bw_mlr.IsBusy) & (RaceCards.CurrentItem != null));
        }

        private bool CanStopMultinomialLogit(object arg)
        {
            return (bw_mlr.IsBusy);
        }

        private bool CanSaveCalculation(object arg)
        {
            if (RaceCards == null)
                return false;

            return IsMlrCalculated((RaceCardModel)RaceCards.CurrentItem);
        }

        private bool CanRemoveStarterFromRace(object arg)
        {
            return ((!bw_rcf.IsBusy) & (!bw_cpv.IsBusy) & (!bw_mlr.IsBusy));
        }

        private bool CanSelectedDatesChanged(object arg) { return true; }
        private bool CanSelectedStarterChanged(object arg) { return true; }

        public ICollectionView RaceCards { get; private set; }

        public race_card SelectedCard
        {
            get
            {
                return this._selectedCard;
            }
            set
            {
                if (this._selectedCard == value) return;
                this._selectedCard = value;
                RaisePropertyChangedEvent("SelectedCard");
            }
        }

        public starter_info SelectedStarter
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

        public void RaceCards_CurrentChanged(object sender, EventArgs e)
        {
            CommandStateChanged();
        }

        public void RaceCards_CurrentChanging(object sender, CurrentChangingEventArgs e) { }

        public DateTime SelectedDate { get; private set; }

        private void SelectedDatesChanged(object sender)
        {
            if (bw_rcf.IsBusy != true)
                bw_rcf.RunWorkerAsync(SelectedDate);
        }

        private void SelectedStarterChanged(object sender)
        {
            if (SelectedStarter != null)
            {
                eventaggregator.GetEvent<ResearchPanelUpdateEvent>().Publish(SelectedStarter.previousRaces);
            }
        }

        private void OnDatabaseChanged(string module)
        {
            if (Globals.DbName.ToLower().CompareTo(uow.dbName) != 0)
            {
                uow.Dispose();
                uow = new UnitOfWork(Globals.DbName.ToLower());
            }
        }

        public ICommand FindRaceCardCommand { get; private set; }
        public ICommand CalculatePredictorVariablesCommand { get; private set; }
        public ICommand RunMultinomialLogitCommand { get; private set; }
        public ICommand StopMultinomialLogitCommand { get; private set; }
        public ICommand SaveCalculationCommand { get; private set; }
        public ICommand RemoveStarterFromRaceCommand { get; private set; }
        public ICommand SelectionDatesChangedCommand { get; private set; }
        public ICommand SelectedStarterChangedCommand { get; private set; }

        #region Calculation options
        public ObservableCollection<string> CalculationOptions
        {
            get
            {
                return this._calculationOptions;
            }
        }

        public string SelectedCalcOption
        {
            get
            {
                return this._selectedCalcOption;
            }
            set
            {
                if (this._selectedCalcOption == value) return;
                this._selectedCalcOption = value;
                RaisePropertyChangedEvent("SelectedCalcOption");
            }
        }
        #endregion

        #region MLR version
        public ObservableCollection<string> MlrVersions
        {
            get
            {
                ObservableCollection<string> vlist = new ObservableCollection<string>();
                string path = Globals.FOLDER_MLR_COEFFICIENTS;
                foreach (string s in Directory.GetDirectories(path))
                {
                    vlist.Add(s.Remove(0, path.Length));
                }

                return vlist;
            }
        }

        public string SelectedMlrVersion
        {
            get
            {
                return this._selectedMlrVersion;
            }
            set
            {
                if (this._selectedMlrVersion == value) return;
                this._selectedMlrVersion = value;
                RaisePropertyChangedEvent("SelectedMlrVersion");
            }
        }
        #endregion

        #region settings
        public void OddsSettingsChanged(object sender)
        {
            if (RaceCards.IsEmpty == false)
            {
                foreach (RaceCardModel racecards in RaceCards)
                    foreach (race_card race in racecards.races)
                        SetBetVerdict(race);

                RefreshStartersTable();
            }
        }

        private bool CanOddsSettingsChanged(object arg) { return true; }

        public bool AutoRefresh
        {
            get
            {
                return this._autorefresh;
            }
            set
            {
                if (this._autorefresh == value) return;
                this._autorefresh = value;

                if (this._autorefresh == false)
                    StopMultinomialLogit(null);

                RaisePropertyChangedEvent("AutoRefresh");
            }
        }

        public int RefreshInterval
        {
            get
            {
                return this._refreshinterval;
            }
            set
            {
                if (this._refreshinterval == value) return;
                this._refreshinterval = value;
                RaisePropertyChangedEvent("RefreshInterval");
            }
        }

        public int OddsDeviationTrigger
        {
            get
            {
                return this._oddsdeviationtrigger;
            }
            set
            {
                if (this._oddsdeviationtrigger == value) return;
                this._oddsdeviationtrigger = value;
                RaisePropertyChangedEvent("OddsDeviationTrigger");
            }
        }

        public int MinimumBackOdds
        {
            get
            {
                return this._minimumbackodds;
            }
            set
            {
                if (this._minimumbackodds == value) return;
                this._minimumbackodds = value;
                RaisePropertyChangedEvent("MinimumBackOdds");
            }
        }

        public int MaximumBackOdds
        {
            get
            {
                return this._maximumbackodds;
            }
            set
            {
                if (this._maximumbackodds == value) return;
                this._maximumbackodds = value;
                RaisePropertyChangedEvent("MaximumBackOdds");
            }
        }

        public int MinimumLayOdds
        {
            get
            {
                return this._minimumlayodds;
            }
            set
            {
                if (this._minimumlayodds == value) return;
                this._minimumlayodds = value;
                RaisePropertyChangedEvent("MinimumLayOdds");
            }
        }

        public int MaximumLayOdds
        {
            get
            {
                return this._maximumlayodds;
            }
            set
            {
                if (this._maximumlayodds == value) return;
                this._maximumlayodds = value;
                RaisePropertyChangedEvent("MaximumLayOdds");
            }
        }

        public ICommand OddsSettingsChangedCommand { get; private set; }
        #endregion

        public bool KeepAlive
        {
            get { return true; }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~CalculateViewModel()   
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

                if (bw_mlr != null)
                {
                    bw_mlr.Dispose();
                    bw_mlr = null;
                }

                if (bw_rcf != null)
                {
                    bw_rcf.Dispose();
                    bw_rcf = null;
                }
            }
        } 
    }
}
