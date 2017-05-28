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
using System.Xml;
using System.Runtime.Serialization;
using Microsoft.Practices.Prism.ViewModel;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.Prism.Commands;
using CsvHelper;
using PonyMLR.Infrastructure;
using PonyMLR.DataAccess;
using PonyMLR.Logit;
using MathNet.Numerics;

namespace PonyMLR.Modules.Test
{
    public class TestViewModel : ViewModelBase, IRegionMemberLifetime, IDisposable
    {
        private IRegionManager regionmanager;
        private IEventAggregator eventaggregator;
        private UnitOfWork uow = new UnitOfWork(Globals.DbName.ToLower());
        private StatusSender statusSender;

        //test bw
        private BackgroundWorker bw = new BackgroundWorker();

        //test setup
        private string _selectedBetCriteria;
        private string _selectedTriggerType;

        //test details
        private ObservableCollection<BetRecordModel> _betRecords;
        private BetRecordModel _selectedBetRecord;

        private ObservableCollection<FavStatModel> _favStatRecords;

        //test results
        private int _totalBets;
        private int _winCount;
        private int _lostCount;
        private double _strikeRate;
        private double _endBalance;

        private bool isLrWin = true;
        private int streak = 0;
        private int lws = 0;
        private int lls = 0;

        private double diminishing_factor = 1;

        // analyse early speed flag
        private bool analyseESpeed = true;

        public TestViewModel(IRegionManager regionmanager, IEventAggregator eventaggregator)
        {
            this.regionmanager = regionmanager;
            this.eventaggregator = eventaggregator;
            this.statusSender = new StatusSender(this.eventaggregator);

            _betRecords = new ObservableCollection<BetRecordModel>();
            _favStatRecords = new ObservableCollection<FavStatModel>();


            //default setup values
            this.TestFile = @"C:\Users\Lau\Documents\Pony\MLR\Development\test.csv";
            this.MaxRecords = 100;
            this.Capital = 5000;
            this.SelectedMlrVersion = MlrVersions.LastOrDefault();
            this.SelectedTrack = "ALL";
            this.SelectedStakingPlan = Globals.EVEN_STAKE;
            this.SelectedWagerType = Globals.WAGER_TYPE_WIN;
            this.SelectedBetCriteria = Globals.BET_CRITERIA_SELECT_ALL;
            this.SelectedTriggerType = Globals.TRIGGER_MLR_SD;
            this.MaxSelectionPerRace = 1;
            this.Fraction = 0.01;
            this.Limit = 0.01;
            this.Trigger = 0.1;
            this.MinOdds = 2;
            this.MaxOdds = 20;

            this.StartTestCommand = new DelegateCommand<object>(this.StartTest, this.CanStartTest);
            this.SelectedBetRecordChangedCommand = new DelegateCommand<object>(this.SelectedBetRecordChanged, this.CanSelectedBetRecordChanged);

            bw.WorkerReportsProgress = true;
            bw.WorkerSupportsCancellation = true;
            bw.DoWork += new DoWorkEventHandler(bw_DoWork);
            bw.ProgressChanged += new ProgressChangedEventHandler(bw_ProgressChanged);
            bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_RunWorkerCompleted);

            eventaggregator.GetEvent<DatabaseSetCompletedEvent>().Subscribe(OnDatabaseChanged, ThreadOption.UIThread);
        }

        private Dictionary<string, double> GetMlrCoefficients(String racetrack)
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

        private void SolveKellyFraction(race_card racecard)
        {
            List<KellyParam> param = new List<KellyParam>();

            int id = 0;
            foreach (starter_info starter in racecard.starters)
            {
                KellyParam kp = new KellyParam();
                kp.Id = id;
                kp.Name = starter.horse_name;
                kp.FairProbability = starter.mlr2.probability_fair;
                kp.ActualOdds = starter.mlr2.odds_actual;

                param.Add(kp);
                id++;
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

        private void PerformBetting(RaceCardModel rcm)
        {
            if (rcm == null)
                return;

            if (rcm.race_date.DayOfWeek == DayOfWeek.Sunday)
                return;

            if (rcm.mlrStepOneCoefficients.Count() == 0)
                return;

            //test temp
            //if ((rcm.racetrack.Value.ToLower().Contains("chester") == true) ||
            //    (rcm.racetrack.Value.ToLower().Contains("goodwood") == true) ||
            //    (rcm.racetrack.Value.ToLower().Contains("hamilton") == true) ||
            //    (rcm.racetrack.Value.ToLower().Contains("newmarket (july)") == true) ||
            //    //(rcm.racetrack.Value.ToLower().Contains("pontefract") == true) ||
            //    (rcm.racetrack.Value.ToLower().Contains("redcar") == true) ||
            //    (rcm.racetrack.Value.ToLower().Contains("sandown") == true) ||
            //    (rcm.racetrack.Value.ToLower().Contains("southwell") == true) ||
            //    (rcm.racetrack.Value.ToLower().Contains("thirsk") == true) ||
            //    (rcm.racetrack.Value.ToLower().Contains("windsor") == true) ||
            //    (rcm.racetrack.Value.ToLower().Contains("wolverhampton") == true)
            //    )
            //    return;
            //if ((rcm.racetrack.Value.ToLower().Contains("lingfield") == true) ||
            //    (rcm.racetrack.Value.ToLower().Contains("kempton") == true) ||
            //    (rcm.racetrack.Value.ToLower().Contains("southwell") == true) ||
            //    (rcm.racetrack.Value.ToLower().Contains("wolverhampton") == true))
            //    return;


            statusSender.SendStatus("Testing: " + rcm.race_date.ToShortDateString() + " " + rcm.racetrack.Value);

            // get trigger
            double trig;
            if (this.SelectedTriggerType == Globals.TRIGGER_MLR_SD)
                trig = rcm.dependentVarStdDeviation;
            else
                trig = this.Trigger;

            rcm.races.Sort((x, y) => TimeSpan.Compare(x.race_time, y.race_time));
            //int rcnt = 0;
            //double pft = 0;
            foreach (race_card rc in rcm.races)
            {
                if ((rc.race_name.Contains("Nursery") == false) && (rc.race_name.Contains("Handicap") == false))
                    continue;

                if (rc.race_number_of_runners <= 3)
                    continue;

                //if ((double)rc.race_distance > 8.5)
                //    continue;

                //if ((rc.race_going.ToLower() == "soft") || (rc.race_going.ToLower() == "heavy") || (rc.race_going.ToLower() == "slow"))
                //    continue;

                //list of MLR Step 2 PVs to calculate
                LogitCalc calc = new LogitCalc(rcm.mlrStepOneCoefficients, rcm.mlrStepTwoCoefficients);

                calc.CalculateAllEntriesPrediction(rc);
             
                // test temp
                //SolveKellyFraction(rc);

                //filter by early speed
                double avgEs = rc.starters.Sum(x => x.early_speed) / rc.starters.Count();

                double last_bal = this._endBalance;
                double frac = this.Fraction;
                List<starter_info> bets = new List<starter_info>();
                if ((this.SelectedBetCriteria != Globals.BET_CRITERIA_SELECT_ALL) && (this.MaxSelectionPerRace > 0))
                {
                    if (this.SelectedBetCriteria == Globals.BET_CRITERIA_SELECT_BY_EDGE)
                        bets = rc.starters.OrderByDescending(x => x.mlr2.odds_deviation).Take(this.MaxSelectionPerRace).ToList();
                    else if (this.SelectedBetCriteria == Globals.BET_CRITERIA_SELECT_BY_PROB)
                        bets = rc.starters.OrderByDescending(x => x.mlr2.probability_fair).Take(this.MaxSelectionPerRace).ToList();
                    else if (this.SelectedBetCriteria == Globals.BET_CRITERIA_SELECT_BY_EDGE_AND_PROB)
                    {
                        List<starter_info> top_edge = rc.starters.OrderByDescending(x => x.mlr2.odds_deviation).Take(this.MaxSelectionPerRace).ToList();
                        List<starter_info> top_prob = rc.starters.OrderByDescending(x => x.mlr2.probability_fair).Take(this.MaxSelectionPerRace).ToList();
                        bets = top_edge.Union(top_prob).ToList();
                    }
                    
                }
                else
                {
                    bets = rc.starters.ToList();
                    //bets = rc.starters.OrderBy(x => x.mlr2.probability_fair).Take(rc.starters.Count()/2).ToList();
                }
                bets = bets.Where(x => x.mlr2.odds_deviation < trig)
                    //.Where(w => w.kellyPercentage > 0.10)
                    .Where(y => y.odds >= this.MinOdds)
                    .Where(z => z.odds <= this.MaxOdds).ToList();

                //double min_odds = bets.Min(x => x.odds);
                //bets = bets.Where(x => x.odds == min_odds).ToList();
                    //bets.Where(x => x.mlr2.odds_deviation < trig)
                    //.Where(u=>u.early_speed >= 0)
                    //.Where(w => w.kellyPercentage > 0.10)
                    //.Where(y => y.odds >= this.MinOdds)
                    //.Where(z => z.odds <= this.MaxOdds).ToList();
                    //.Where(w => w.early_speed > 3 /*|| w.early_speed <= 1*/).ToList();

                // no bet
                if (bets.Count() == 0)
                    continue;

                //foreach (starter_info b in bets)
                //{
                //    List<starter_info> ls = rc.starters.Where(x => x.stall < 6).ToList();
                //    if (ls.Any(x => x.early_speed > 3) == true)
                //        b.stall = 0;
                //}

                // ********************* LAY

                foreach (starter_info b in bets)
                {
                    int hkey = 0;
                    horse_info hi = uow.HorseInfoRepository.Get(x=>x.horse_name == b.horse_name).First();
                    if (hi != null)
                        hkey = hi.horse_id;

                    List<race_result> rr = uow.RaceResultRepository.Get(x => x.horse_key == hkey).Where(z=>z.race_info.race_date < rcm.race_date).OrderByDescending(y => y.result_id).ToList();

                    b.lstakeProfit = Convert.ToDouble(rr.Where(x => x.finishing_position == 1).Sum(y => (y.odds + 1))) - rr.Count();
                    b.hpOverRace = rr.First().finishing_position;// b.lstakeProfit / rr.Count();                  

                    if (b.hpOverRace != 1)
                    {
                        if (rr.Count() > 1)
                        {
                            double fp = rr.Skip(1).First().finishing_position;
                            if (fp == 1)
                                b.hpOverRace = -1;
                        }
                    }
                    //if (b.lstakeProfit < -10)
                    //    b.stall = 0;

                    //if (rr.Count() < 2)
                    //    b.stall = 0;

                    //bool notallone = rr.Any(x => x.finishing_position != 1);
                    //if (notallone)
                    //    b.stall = 0;

                    //if (rc.starters.Min(x => x.mlr2.odds_fair) == b.mlr2.odds_fair)
                    //{
                    //    b.stall = 0;
                    //    continue;
                    //}

                    //if (b.early_speed >= 4)
                    //{
                    //    b.stall = 0;
                    //}
                    //if (b.early_speed > 3)
                    //{
                    //    if (rc.race_distance > uow.RaceTrackRepository.Get(x => x.track_id == rcm.racetrack.Key).First().straight_distance)
                    //    {
                    //        if (rc.starters.Where(x => x.stall < 8).Where(y => y.early_speed > 3).Count() != 0)
                    //        {
                    //            if (b.stall < 8)
                    //                b.stall = 0;
                    //        }
                    //        else
                    //            b.stall = 0;

                    //        if (rc.starters.Where(x => x.early_speed > 3).Count() < 3)
                    //            b.stall = 0;
                    //    }
                    //    else
                    //        b.stall = 0;
                    //    //else
                    //    //{
                    //    //    if (rc.starters.Where(x => x.early_speed > 3).Count() <= (rc.race_number_of_runners / 2))
                    //    //        b.stall = 0;
                    //    //}
                    //}
                    //else
                    //{
                    //    if (rc.race_number_of_runners < 10)
                    //        b.stall = 0;

                    //    //if (rc.race_distance > uow.RaceTrackRepository.Get(x => x.track_id == rcm.racetrack.Key).First().straight_distance)
                    //    //{
                    //    //    if (rc.starters.Where(x => x.early_speed >= 3).Any(y => y.stall < (rc.race_number_of_runners / 2)) == true)
                    //    //        b.stall = 0;
                    //    //}

                    //    //if (rc.starters.Where(x=>x.early_speed < 2).Count() < 4)
                    //    //    b.stall = 0;

                    //    if (rc.starters.Where(x => x.early_speed >= 3).Count() > 1)
                    //    {
                    //        b.stall = 0;
                    //    }
                    //}
                }

                bets = bets.Where(x => x.stall != 0).ToList();

                //*********************** BACK
                //bets = new List<starter_info>() { bets.OrderBy(x => x.mlr2.odds_fair).First() };
                //if (bets.Where(x => x.mlr2.odds_fair > 5).Count() < this.MaxSelectionPerRace)
                //    continue;

                //if ((bets.Max(x => x.mlr2.odds_fair) - bets.Min(x => x.mlr2.odds_fair)) > 1)
                //    continue;

                //if (bets.Any(x => x.mlr2.odds_deviation >= 0.5) == false)
                //    continue;

                //if (bets.Any(x => x.mlr2.odds_fair > 6) == true)
                //    continue;

                //starter_info bf = bets.OrderByDescending(x=>x.early_speed).First();
                //if (bf.early_speed > 3.5)
                //{                  
                //    if (rc.starters.Any(x => x.early_speed > bf.early_speed) == true)
                //        continue;

                //    double ediff = rc.starters.OrderByDescending(x => x.early_speed).First().early_speed - rc.starters.OrderByDescending(x => x.early_speed).Skip(1).First().early_speed;
                //    if (ediff < 1)
                //        continue;
                //    else if (ediff < 2)
                //    {
                //        if (rc.starters.Where(x => x.early_speed >= 3).Count() > 1)
                //            continue;
                //    }

                //    // fast pace
                //    //if (rc.starters.Where(x => x.early_speed >= 4).Count() >= 4)
                //    //    continue;

                //    ////is widely drawn?
                //    //if (rc.race_distance > uow.RaceTrackRepository.Get(x => x.track_id == rcm.racetrack.Key).First().straight_distance)
                //    //{
                //    //    if (bf.stall > 7)
                //    //        continue;

                //    //    if (bf.stall > 4)
                //    //    {
                //    //        if (rc.starters.Where(x => x.early_speed > 3).Where(y => y.stall < bf.stall).Count() > 1)
                //    //            continue;
                //    //    }
                //    //}

                //    //if (rc.starters.Where(x => x.early_speed >= 4).Count() < 4)
                //    //    continue;

                //    ////is widely drawn?
                //    //if (rc.race_distance > uow.RaceTrackRepository.Get(x => x.track_id == rcm.racetrack.Key).First().straight_distance)
                //    //{
                //    //    if (bf.stall <= 7)
                //    //        continue;

                //    //    if (bf.stall <= 4)
                //    //    {
                //    //        if (rc.starters.Where(x => x.early_speed > 3).Where(y => y.stall < bf.stall).Count() <= 1)
                //    //            continue;
                //    //    }
                //    //}

                //    bets = new List<starter_info>() { bf };
                //}
                //else /*if (bf.early_speed <= 2)*/
                //{
                //    continue;
                //    //if (rc.race_number_of_runners > 6)
                //    //{
                //    //    // slow or even pace
                //    //    if (rc.starters.Where(x => x.early_speed >= 4).Count() < 3)
                //    //        continue;

                //    //}

                //    //// big field BMK
                //    //if (rc.race_number_of_runners > 7)
                //    //{
                //    //    if (bf.early_speed < 2)
                //    //        continue;
                //    //}

                //    //if (rc.race_number_of_runners <= 6)
                //    //{
                //    //    // slow or even pace
                //    //    if (rc.starters.Where(x => x.early_speed >= 4).Count() >= 3)
                //    //        continue;

                //    //}

                //    //// big field BMK
                //    //if (rc.race_number_of_runners <= 7)
                //    //{
                //    //    if (bf.early_speed >= 2)
                //    //        continue;
                //    //}
                //}
                //else if (bf.early_speed < 2)
                //{
                //    continue;
                //}                

                //if ((bf.odds < 4) && (bf.mlr2.odds_fair > 2))
                //{
                //    continue;
                //}

                //if ((bf.odds >= 4) && (bf.mlr2.odds_deviation < 1))
                //{
                //    continue;
                //}

                //if (bf.mlr2.odds_fair > 2.5)
                //    continue;

                if ((this.SelectedStakingPlan == Globals.EVEN_STAKE) || (this.SelectedStakingPlan == Globals.SQUARE_ROOT))
                {
                    frac = this.Fraction;
                }
                else if (this.SelectedStakingPlan == Globals.FIXED_RATIO)
                {
                    foreach (starter_info b in bets) b.kellyPercentage = 1;
                    //double tf = bets.Count() * frac;
                    //if (tf > this.Limit)
                    //{
                    //    frac = this.Limit / bets.Count();
                    //}

                }
                else if (this.SelectedStakingPlan == Globals.KELLY_FRACTION)
                {
                    //SolveKellyFraction(rc);

                    // adjust kelly by fraction limit per race
                    double kls = bets.Sum(x => x.kellyPercentage);
                    if (kls > 0)
                    {
                        double lim = kls * frac;
                        if (lim > this.Limit)
                        {
                            frac = this.Limit;
                            foreach (starter_info b in bets) b.kellyPercentage = (b.kellyPercentage / kls);
                        }
                    }
                }

                double balbefore = 0;
                if (BetRecords.Count() != 0)
                    balbefore = this._endBalance;

                foreach (starter_info st in bets)
                {
                    if (st.lstakeProfit < -10)
                        continue;

                    //if ((st.hpOverRace != 1) && (st.hpOverRace != -1))
                    //    continue;

                    if ((st.hpOverRace != -1) && (st.hpOverRace != 1))
                        continue;

                    //if (analyseESpeed == true)
                    //{
                    //    if (st.early_speed < 2)
                    //        continue;
                    //}

                    //if (st.early_speed >= 4)
                    //{
                    //    int frCount = rc.starters.Where(x => x.early_speed >= 4).Count();
                    //    if (frCount > 3)
                    //    {
                    //        frCount = 0;
                    //        continue;
                    //    }
                    //}

                    //if (st.finishing_position == 1)
                    //{

                    //}

                    double multiplier = st.kellyPercentage * frac;// *diminishing_factor;                 
                    double wager;                   
                    if (this.SelectedStakingPlan == Globals.EVEN_STAKE)
                    {
                        wager = this.Capital * frac;
                    }
                    else if (this.SelectedStakingPlan == Globals.SQUARE_ROOT)
                    {
                        wager = (this.Capital * frac) + (last_bal > this.Capital ? Math.Sqrt(last_bal - this.Capital) : 0);
                    }
                    else
                    {
                        wager = last_bal * multiplier;
                    }
                    double profit = 0;

                    // wager fraction by odds
                    //if (st.mlr2.odds_actual < 6)
                    //{
                    //    wager = wager * 1;
                    //}
                    //else if (st.mlr2.odds_actual < 10)
                    //{
                    //    wager = wager * 0.7;
                    //}
                    //else
                    //{
                    //    wager = wager * 0.4;
                    //}

                    //poisson test
                    //int timeframe = 30;
                    //double avgWin = 2.97;
                    //if (this._totalBets != 0)
                    //    avgWin = ((double)this._winCount / (double)this._totalBets) * (double)timeframe;
                    //if (avgWin < 1)
                    //    avgWin = 1;
                    //int k = BetRecords.OrderByDescending(x => x.race_date).Take(timeframe).Where(y => y.is_win == true).Count();
                    //MathNet.Numerics.Distributions.Poisson pDist = new MathNet.Numerics.Distributions.Poisson(avgWin);
                    //double prob = pDist.Probability(k + 1);
                    //wager = wager * prob;

                    // add new entry in bet record
                    BetRecordModel brm = new BetRecordModel();
                    brm.race_date = rcm.race_date;
                    brm.race_time = rc.race_time;
                    brm.race_distance = (double)rc.race_distance;
                    brm.racetrack = rcm.racetrack.Value;
                    brm.mlr_version = SelectedMlrVersion;
                    brm.description = st.horse_name;
                    brm.horse_age = st.horse_age;
                    brm.horse_levelstake_p = st.lstakeProfit;
                    brm.hpoverrace = st.hpOverRace;
                    brm.fair_odds = st.mlr2.odds_fair;
                    brm.actual_odds = st.mlr2.odds_actual;
                    brm.edge = st.mlr2.odds_deviation;
                    brm.stake = wager;
                    brm.is_win = Utils.IsWinner(st.finishing_position);
                    brm.is_place = Utils.IsPlacer(st.finishing_position, rc.race_number_of_runners);

                    brm.early_speed = st.early_speed;
                    brm.averageEs = avgEs;
                    string esstr = "";
                    foreach (starter_info si in rc.starters.OrderBy(x=>x.early_speed))
                    {
                        esstr = esstr + "," + si.early_speed.ToString("##0.00");
                    }
                    brm.esString = esstr.TrimStart(',');

                    // wager type
                    double win_wager = 0;
                    double place_wager = 0;
                    if (this.SelectedWagerType == Globals.WAGER_TYPE_WIN)
                    {
                        win_wager = wager;
                    }
                    else if (this.SelectedWagerType == Globals.WAGER_TYPE_PLACE)
                    {
                        place_wager = wager;
                    }
                    else
                    {
                        win_wager = wager / 2;
                        place_wager = wager / 2;
                    }

                    // win, place or lose
                    if (brm.is_win == true)
                    {
                        if (brm.actual_odds > 3)
                            profit = ((wager / 2) * -1 * (st.odds + 0.2)) + ((wager / 2) * -1 * Utils.GetPlaceOdds(st.odds, rc.race_number_of_runners, true));
                        else
                            profit = wager * -1 * (st.odds + 0.2);
                        //profit = ((win_wager * st.odds) + (place_wager * Utils.GetPlaceOdds(st.odds, rc.race_number_of_runners, true)));
                        
                    }
                    else if (brm.is_place == true)
                    {
                        if (brm.actual_odds > 3)
                            profit = (wager/2) + ((wager/2) * -1 * Utils.GetPlaceOdds(st.odds, rc.race_number_of_runners, true));
                        else
                            profit = wager;
                        //profit = wager * -1; //-1 * ((win_wager * -1) + (place_wager * Utils.GetPlaceOdds(st.odds, rc.race_number_of_runners, true)));
                    }
                    else
                    {
                        profit = wager * 0.85;
                        //profit = wager * -1;                   
                    }

                    if (profit > 0)
                    {                       
                        this._winCount++;
                        if (this.isLrWin)
                        {
                            this.streak++;
                        }
                        else
                        {
                            this.streak = 1;
                            this.isLrWin = true;
                        }
                        if (this.lws < this.streak)
                            this.lws = this.streak;
                    }
                    else
                    {
                        this._lostCount++;
                        if (!this.isLrWin)
                        {
                            this.streak++;
                        }
                        else
                        {
                            this.streak = 1;
                            this.isLrWin = false;
                        }
                        if (this.lls < this.streak)
                            this.lls = this.streak;
                    }

                    this._endBalance = this._endBalance + profit;

                    brm.profit = profit;
                    brm.balance = this._endBalance;
                    // add bet record
                    DefaultDispatcher dispatcher = new DefaultDispatcher();
                    Action<BetRecordModel> addMethod = BetRecords.Add;
                    dispatcher.BeginInvoke(addMethod, brm);
                 
                    this._totalBets++;

                    //rcnt++;
                    //pft += profit;
                }

                if (BetRecords.Count != 0)
                {
                    if (this._endBalance > balbefore)
                        diminishing_factor = diminishing_factor * 0.85;
                    else
                        diminishing_factor = 1;
                }

                //if ((rcnt > 1) && (pft > 0))
                //    break;
            }
        }

        private void FavWinStatCalc()
        {
            //if (rcm == null)
            //    return;

            int allrc = 0;
            int allfw = 0;

            try
            {                
                List<DateTime> dateToTest = new List<DateTime>();
                DateTime sdt = DateTime.Parse("01/01/2013");
                DateTime edt = DateTime.Parse("31/10/2015");

                DateTime latestDt = sdt;
                while (!latestDt.Date.Equals(edt))
                {
                    latestDt = latestDt.AddDays(1);
                    dateToTest.Add(latestDt);
                }

                foreach (DateTime dt in dateToTest)
                {
                    List<race_result> winners = uow.RaceResultRepository.Get(x => x.race_info.race_date == dt)
                        //.Where(z=>z.race_info.race_name.Contains("Handicap") == true)
                        .Where(y => y.is_winner == true).ToList();

                    if (winners.Count() == 0)
                        continue;

                    int rcount = winners.Count();
                    int fwcount = winners.Where(x => x.is_favourite == true).Count();


                    FavStatModel fsm = new FavStatModel();
                    fsm.race_date = dt.ToShortDateString();
                    fsm.race_count = rcount;
                    fsm.fav_win_cnt = fwcount;
                    fsm.fav_win_percentage = ((double)fwcount) / ((double)rcount);

                    // add bet record
                    DefaultDispatcher dispatcher = new DefaultDispatcher();
                    Action<FavStatModel> addMethod = FavStatRecords.Add;
                    dispatcher.BeginInvoke(addMethod, fsm);

                    ////cumulative
                    //allrc = allrc + rcount;
                    //allfw = allfw + fwcount;

                    //FavStatModel csm = new FavStatModel();
                    //csm.race_date = "cumulative";
                    //csm.race_count = allrc;
                    //csm.fav_win_cnt = allfw;
                    //csm.fav_win_percentage = ((double)allfw) / ((double)allrc);

                    //// add bet record
                    //DefaultDispatcher dispatcher2 = new DefaultDispatcher();
                    //Action<FavStatModel> addMethod2 = FavStatRecords.Add;
                    //dispatcher2.BeginInvoke(addMethod2, csm);
                }
            }
            catch (Exception e)
            {
                statusSender.SendStatus(e.Message);
            }

        }
        private void UpdateResults()
        {
            RaisePropertyChangedEvent("TotalBets");
            RaisePropertyChangedEvent("WinCount");
            RaisePropertyChangedEvent("LostCount");
            RaisePropertyChangedEvent("StrikeRate");
            RaisePropertyChangedEvent("EndBalance");
        }

        private void ClearBets()
        {
            BetRecords.Clear();
            WinCount = 0;
            LostCount = 0;
            TotalBets = 0;
            StrikeRate = 0;
            EndBalance = this.Capital;
        }

        #region backgroundWorker
        //pv calculator worker
        private void bw_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;

            //FavWinStatCalc();

            //return;

            // sanity check
            if (MinOdds > MaxOdds)
            {
                statusSender.SendStatus("Max Odds cannot be less than Min Odds. Test not started.");
                return;
            }

            // initialize
            int r_cnt = 0;
            RaceCardModel rcm = null;
            race_card rc = null;

            try
            {
                using (var sr = new StreamReader(TestFile))
                {
                    var reader = new CsvReader(sr);

                    while(reader.Read())
                    {
                        if (r_cnt > this.MaxRecords)
                        {
                            if (rcm != null)
                                PerformBetting(rcm);

                            break;
                        }

                        DateTime date = Convert.ToDateTime(reader.GetField<string>("race_date"));
                        string track = reader.GetField<string>("track");
                        string going = reader.GetField<string>("going_description");
                        string rname = reader.GetField<string>("race_name");

                        // skip to next entry if it is not ALL nor the racetrack selected
                        string selected_track = this.SelectedTrack;
                        if ((selected_track != "ALL") && (selected_track.Contains(track) != true))
                            continue;
                        if ((selected_track.ToLower().Contains("newmarket") == true) && (selected_track != track))
                            continue;

                        track = Utils.RemoveStringInBracket(reader.GetField<string>("track"));

                        // check for lingfield AW or turf
                        if (selected_track.ToLower().Contains("lingfield") == true)
                        {
                            if (going.CompareTo(Globals.AW_GOING_STANDARD) == 0 ||
                                going.CompareTo(Globals.AW_GOING_FAST) == 0 || going.CompareTo(Globals.AW_GOING_STANDARD_TO_FAST) == 0 ||
                                going.CompareTo(Globals.AW_GOING_STANDARD_TO_SLOW) == 0 || going.CompareTo(Globals.AW_GOING_SLOW) == 0)
                            {
                                if (this.SelectedTrack.ToLower().Contains("turf") == true)
                                    continue;
                            }
                            else
                            {
                                if (this.SelectedTrack.ToLower().Contains("turf") == false)
                                    continue;
                            }
                        }

                        if (rcm != null)
                        {
                            if (rcm.races.Count() != 0)
                            {
                                if ((track != rcm.racetrack.Value) ||
                                    (going != rcm.races.Last().race_going) ||
                                    (date.CompareTo(rcm.race_date) != 0))
                                {
                                    // this is record of a new meeting or different race surface, do betting calculation of previous meeting
                                    PerformBetting(rcm);
                                    rcm = null;
                                }
                            }
                        }
                        
                        // new meeting
                        if (rcm == null)
                        {
                            IEnumerable<racetrack> trackobj = uow.RaceTrackRepository.Get();
                            racetrack to = trackobj.Where(x => x.track_name.Contains(track) == true).First();
                            int tkey = 0;
                            if (to != null)
                                tkey = to.track_id;
                            rcm = new RaceCardModel(date, new KeyValuePair<int, string>(tkey, track));
                            rcm.races = new List<race_card>();

                            try
                            {
                                //special case for Lingfield              
                                if (track.ToLower().CompareTo("lingfield") == 0)
                                {                                
                                    if (going.CompareTo(Globals.AW_GOING_STANDARD) == 0 ||
                                        going.CompareTo(Globals.AW_GOING_FAST) == 0 || going.CompareTo(Globals.AW_GOING_STANDARD_TO_FAST) == 0 ||
                                        going.CompareTo(Globals.AW_GOING_STANDARD_TO_SLOW) == 0 || going.CompareTo(Globals.AW_GOING_SLOW) == 0)
                                        track = track + "_AWT";
                                    else
                                        track = track + "_TURF";
                                }

                                //special case for Newmarket
                                if (track.ToLower().CompareTo("newmarket") == 0)
                                {
                                    int month = date.Month;
                                    if (month == 6 || month == 7 || month == 8)
                                        track = track + "_JULY";
                                    else
                                        track = track + "_ROWLEY";
                                }
                            }
                            catch { }

                            rcm.mlrStepOneCoefficients = GetMlrCoefficients(track + "_STEP1");
                            rcm.mlrStepTwoCoefficients = GetMlrCoefficients(track + "_STEP2");
                            if (rcm.mlrStepTwoCoefficients.Count() == 0)
                                rcm.dependentVarStdDeviation = GetMlrDvStdDeviation(track + "_STEP1");
                            else
                                rcm.dependentVarStdDeviation = GetMlrDvStdDeviation(track + "_STEP2");
                        }

                        // new race
                        if (rc == null)
                        {
                            rc = new race_card();
                            rc.race_time = TimeSpan.Parse(reader.GetField<string>("race_time"));
                            rc.race_going = going;
                            rc.race_name = rname;
                        }

                        // get starter info from file
                        starter_info st = new starter_info();
                        st.horse_name = reader.GetField<string>("horse_name");
                        st.horse_age = reader.GetField<int>("horse_age");
                        st.stall = reader.GetField<int>("stall");
                        st.odds = reader.GetField<double>("odds");
                        st.is_favourite = Convert.ToBoolean(reader.GetField<string>("fav").ToLower());
                        st.finishing_position = reader.GetField<int>("place");


                        //get early speed////////////////////////////////////////////////////////////////////////////////
                        if (analyseESpeed == true)
                        {
                            List<race_result> previousRaces = new List<race_result>();
                            // get horse id
                            int hId = 0;
                            try
                            {
                                string brname = Utils.RemoveStringInBracket(st.horse_name);
                                List<horse_info> hil = new List<horse_info>(uow.HorseInfoRepository.Get(x => x.horse_name.ToLower().Replace("'", "").Contains(brname.ToLower().Replace("'", "")) == true)
                                                                                .Where(y => Utils.RemoveStringInBracket(y.horse_name.ToLower().Replace("'", "")).Equals(brname.ToLower().Replace("'", "")) == true)
                                                                                );
                                if (hil.Count() > 1)
                                {
                                    string bracketedstr = Utils.GetStringInBracket(SelectedBetRecord.description);
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
                                int yof = date.Year - st.horse_age;
                                previousRaces.AddRange(uow.HorseInfoRepository
                                    .Get(w => w.horse_id == hId).FirstOrDefault().race_result
                                    .Where(x => x.race_info.race_number_of_runners > 1) //ignore walk over race
                                    .Where(y => y.race_info.race_date.Year > yof)   //race must be after the year it was foaled
                                    .Where(z => z.race_info.race_date < date)
                                    .OrderByDescending(z => z.result_id)
                                    .Take(3));

                                //calculate early speed
                                double total = 0;
                                double count = 0;
                                foreach (race_result rr in previousRaces)
                                {
                                    total = total + Utils.CommentToEarlySpeedFigure(rr.race_comment);
                                    count++;
                                }
                                if (count != 0)
                                    st.early_speed = total / count;
                                else
                                    st.early_speed = 0;

                                //if (previousRaces != null && previousRaces.Count() != 0)
                                //{
                                //    string pcomment = previousRaces.First().race_comment;
                                //    if (pcomment != null)
                                //    {
                                //        if (pcomment.Contains("hung left") == true ||
                                //            pcomment.Contains("hung right") == true ||
                                //            pcomment.Contains("edged left") == true ||
                                //            pcomment.Contains("edged right") == true ||
                                //            pcomment.Contains("raced wide") == true ||
                                //            pcomment.Contains("eased") == true)
                                //            st.early_speed = -1;
                                //    }
                                //}
                            }
                        }

                        foreach (KeyValuePair<string, double> kvp in rcm.mlrStepOneCoefficients)
                        {
                            double pv = reader.GetField<double>(kvp.Key);
                            st.predictorVariables.Add(kvp.Key, pv);
                        }

                        // add to race card
                        rc.starters.Add(st);
                        int st_count = reader.GetField<int>("number_of_runners");
                        double race_dist = reader.GetField<double>("horse_age");
                        if (rc.starters.Count() == st_count)
                        {
                            // this is the last starter of the race, add to race card model
                            rc.race_number_of_runners = st_count;
                            rc.race_distance = (decimal)race_dist;
                            rcm.races.Add(rc);
                            rc = null;
                            r_cnt++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                statusSender.SendStatus("Error: " + ex.Message);
            }

            // overall results
            if (this._totalBets != 0)
                this._strikeRate = (double)this._winCount / (double)this._totalBets;

        }

        private void bw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
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
                statusSender.SendStatus("Done!");
                UpdateResults();
            }

            eventaggregator.GetEvent<DatabaseUnlockedEvent>().Publish("TestModule");
        }

        private void bw_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {

        }
        #endregion

        private bool CanStartTest(object arg)
        {
            return true;
        }

        private bool CanSelectedBetRecordChanged(object arg)
        {
            return true;
        }

        private void StartTest(object arg)
        {
            if (uow == null) return;

            if (bw.IsBusy != true)
            {
                ClearBets();

                eventaggregator.GetEvent<DatabaseLockedEvent>().Publish("TestModule");
                bw.RunWorkerAsync();
                statusSender.SendStatus("Starting...");
            }
        }

        private void SelectedBetRecordChanged(object sender)
        {
            if (SelectedBetRecord != null)
            {
                List<race_result> previousRaces = new List<race_result>();
                // get horse id
                int hId = 0;
                try
                {
                    string brname = Utils.RemoveStringInBracket(SelectedBetRecord.description);
                    List<horse_info> hil = new List<horse_info>(uow.HorseInfoRepository.Get(x => x.horse_name.ToLower().Replace("'", "").Contains(brname.ToLower().Replace("'", "")) == true)
                                                                    .Where(y => Utils.RemoveStringInBracket(y.horse_name.ToLower().Replace("'", "")).Equals(brname.ToLower().Replace("'", "")) == true)
                                                                    );
                    if (hil.Count() > 1)
                    {
                        string bracketedstr = Utils.GetStringInBracket(SelectedBetRecord.description);
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
                    int yof = SelectedBetRecord.race_date.Year - SelectedBetRecord.horse_age;
                    previousRaces.AddRange(uow.HorseInfoRepository
                        .Get(w => w.horse_id == hId).FirstOrDefault().race_result
                        .Where(x => x.race_info.race_number_of_runners > 1) //ignore walk over race
                        .Where(y => y.race_info.race_date.Year > yof)   //race must be after the year it was foaled
                        .OrderByDescending(z => z.result_id)
                        .Take(Globals.MAX_STARTER_PREVIOUS_RACES));
                }

                eventaggregator.GetEvent<ResearchPanelUpdateEvent>().Publish(previousRaces);
            }
        }

        private void OnDatabaseChanged(object db)
        {
            if (db != null)
            {
                try
                {
                    uow = (UnitOfWork)db;
                }
                catch (Exception e)
                {
                    statusSender.SendStatus("Invalid DB: " + e.Message);
                }
            }

            if (uow != null)
            {
                RaisePropertyChangedEvent("TrackSelectionsList");
            }             
        }

        //setup parameters
        public string TestFile { get; private set; }
        public int MaxRecords { get; private set; }
        public double Capital { get; private set; }
        public string SelectedTrack { get; private set; }
        public string SelectedMlrVersion { get; private set; }
        public string SelectedStakingPlan { get; private set; }
        public string SelectedWagerType { get; private set; }
        public double Fraction { get; private set; }
        public double Limit { get; private set; }
        public string SelectedBetCriteria
        { 
            get
            {
                return this._selectedBetCriteria;
            }
            private set
            {
                if (value != this._selectedBetCriteria) this._selectedBetCriteria = value;
                RaisePropertyChangedEvent("SelectedBetCriteria");
            }
        }
        public int MaxSelectionPerRace { get; private set; }
        public double MaxOdds { get; private set; }
        public double MinOdds { get; private set; }
        public string SelectedTriggerType
        {
            get
            {
                return this._selectedTriggerType;
            }
            private set
            {
                if (value != this._selectedTriggerType) this._selectedTriggerType = value;
                RaisePropertyChangedEvent("SelectedTriggerType");
            }
        }
        public double Trigger { get; private set; }
        public List<string> TrackSelectionsList
        {
            get
            {
                List<string> selections = new List<string>();
                if (uow.dbName != "")
                {
                    selections.Add("ALL");
                    List<string> tlist = uow.RaceTrackRepository.Get(x => x.flat_characteristic != "Inactive").Select(y => y.track_name).ToList();
                    foreach (string t in tlist)
                    {
                        // add 2 entries for lingfield (aw and turf)
                        if (t.ToLower() == "lingfield")
                        {
                            selections.Add(t + " (AW)");
                            selections.Add(t + " (Turf)");
                        }
                        else
                            selections.Add(t);
                    }
                }
                return selections;
            }
        }
        public List<string> StakingPlanSelectionsList
        {
            get
            {
                List<string> selections = new List<string>();
                selections.Add(Globals.EVEN_STAKE);
                selections.Add(Globals.FIXED_RATIO);
                selections.Add(Globals.SQUARE_ROOT);
                selections.Add(Globals.KELLY_FRACTION);

                return selections;
            }
        }
        public List<string> WagerTypeSelectionsList
        {
            get
            {
                List<string> selections = new List<string>();
                selections.Add(Globals.WAGER_TYPE_WIN);
                selections.Add(Globals.WAGER_TYPE_PLACE);
                selections.Add(Globals.WAGER_TYPE_EACHWAY);

                return selections;
            }
        }
        public List<string> TriggerTypeSelectionsList
        {
            get
            {
                List<string> selections = new List<string>();
                selections.Add(Globals.TRIGGER_MLR_SD);
                selections.Add(Globals.TRIGGER_CUSTOM);

                return selections;
            }
        }
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
        public List<string> BetCriteriaSelectionsList
        {
            get
            {
                List<string> selections = new List<string>();
                selections.Add(Globals.BET_CRITERIA_SELECT_ALL);
                selections.Add(Globals.BET_CRITERIA_SELECT_BY_EDGE);
                selections.Add(Globals.BET_CRITERIA_SELECT_BY_PROB);
                selections.Add(Globals.BET_CRITERIA_SELECT_BY_EDGE_AND_PROB);

                return selections;
            }
        }

        //detail parameters
        public ObservableCollection<BetRecordModel> BetRecords
        {
            get
            {
                return this._betRecords;
            }
            set
            {
                if (this._betRecords == value) return;
                this._betRecords = value;
                RaisePropertyChangedEvent("BetRecords");
            }
        }

        public ObservableCollection<FavStatModel> FavStatRecords
        {
            get
            {
                return this._favStatRecords;
            }
            set
            {
                if (this._favStatRecords == value) return;
                this._favStatRecords = value;
                RaisePropertyChangedEvent("FavStatRecords");
            }
        }

        public BetRecordModel SelectedBetRecord
        {
            get
            {
                return this._selectedBetRecord;
            }
            set
            {
                if (this._selectedBetRecord == value) return;
                this._selectedBetRecord = value;
                RaisePropertyChangedEvent("SelectedBetRecord");
            }
        }

        //result parameters
        public int TotalBets
        {
            get
            {
                return this._totalBets;
            }
            set
            {
                if (this._totalBets == value) return;
                this._totalBets = value;
                RaisePropertyChangedEvent("TotalBets");
            }
        }

        public int WinCount
        {
            get
            {
                return this._winCount;
            }
            set
            {
                if (this._winCount == value) return;
                this._winCount = value;
                RaisePropertyChangedEvent("WinCount");
            }
        }

        public int LostCount
        {
            get
            {
                return this._lostCount;
            }
            set
            {
                if (this._lostCount == value) return;
                this._lostCount = value;
                RaisePropertyChangedEvent("LostCount");
            }
        }

        public double StrikeRate
        {
            get
            {
                return this._strikeRate;
            }
            set
            {
                if (this._strikeRate == value) return;
                this._strikeRate = value;
                RaisePropertyChangedEvent("StrikeRate");
            }

        }

        public double EndBalance
        {
            get
            {
                return this._endBalance;
            }
            set
            {
                if (this._endBalance == value) return;
                this._endBalance = value;
                RaisePropertyChangedEvent("EndBalance");
            }

        }

        //commands
        public ICommand StartTestCommand { get; private set; }
        public ICommand SelectedBetRecordChangedCommand { get; private set; }

        public bool KeepAlive
        {
            get { return true; }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~TestViewModel()   
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

