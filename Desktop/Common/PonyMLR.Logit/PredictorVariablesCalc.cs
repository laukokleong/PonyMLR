using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using MathNet.Numerics;
using PonyMLR.DataAccess;
using PonyMLR.Infrastructure;

namespace PonyMLR.Logit
{
    public class PredictorVariablesCalc : IDisposable
    {
        private DateTime dt;
        private KeyValuePair<int, String> racetrack;
        private List<string> pvList;
        private UnitOfWork uow;

        //single race general info
        private string race_name;
        private int race_class;
        private decimal distance;
        private int prize_money;
        private string going;
        private int num_runners;

        //temporary holder of last race starters' stats
        private Dictionary<string, Dictionary<string, Tuple<double, double>>> lrst_stats;

        public PredictorVariablesCalc(DateTime dt, KeyValuePair<int, String> racetrack, List<string> pvList)
        {
            this.dt = dt;
            this.racetrack = racetrack;
            this.pvList = pvList;
            this.uow = new UnitOfWork(Globals.DbName.ToLower());

            this.lrst_stats = new Dictionary<string, Dictionary<string, Tuple<double, double>>>();
        }

        public bool CalculateAllEntriesPvs(race_card racecard)
        {
            //skip calculating races with first time out starter(s)
            if (racecard.starters.Any(s => s.previousRaces.Count == 0) == true)
                return true;

            SetSingleRaceGeneralInfo(racecard.race_name,
                                        racecard.race_class,
                                        racecard.race_distance,
                                        racecard.race_prize_money,
                                        racecard.race_going,
                                        racecard.race_number_of_runners);

            List<MethodInfo> methods = new List<MethodInfo>(this.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance));
            foreach (MethodInfo method in methods)
            {
                PredictorVariablesAtt attr = (PredictorVariablesAtt)Attribute.GetCustomAttribute(method, typeof(PredictorVariablesAtt));
                if (attr == null)
                    continue;

                if (pvList.Any(p => p.CompareTo(attr.Name) == 0) != false)
                {
                    foreach (starter_info starter in racecard.starters)
                    {
                        object pvVal = null;

                        if (attr.Category.CompareTo(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS) != 0)
                            pvVal = method.Invoke(this, new object[] { starter });
                        else
                            pvVal = method.Invoke(this, new object[] { starter, new List<starter_info>(racecard.starters) });

                        if (starter.predictorVariables.Any(p => p.Key.CompareTo(attr.Name) == 0) == false)
                            starter.predictorVariables.Add(attr.Name, (double)pvVal);
                    }
                }
                else
                    continue;
            }

            CLearSingleRaceGeneralInfo();
            ClearLastRaceStartersStats();

            return false;
        }

        #region Private Utilities
        private void SetSingleRaceGeneralInfo(string race_name, int race_class, decimal distance, int prize_money, string going, int num_runners)
        {
            this.race_name = race_name;
            this.race_class = race_class;
            this.distance = distance;
            this.prize_money = prize_money;
            this.going = going;
            this.num_runners = num_runners;
        }

        private void CLearSingleRaceGeneralInfo()
        {
            this.race_name = "";
            this.race_class = 0;
            this.distance = 0;
            this.prize_money = 0;
            this.going = null;
            this.num_runners = 0;
        }

        private Tuple<double, double> GetLastRaceStarterStats(string st, string lrst)
        {
            // look for current horse
            if (this.lrst_stats.ContainsKey(st))
            {
                // look for its last race contender
                if (this.lrst_stats[st].ContainsKey(lrst))
                {
                    return this.lrst_stats[st][lrst];
                }
            }

            return null;
        }

        private void UpdateLastRaceStarterStats(string st, string lrst, double win, double place)
        {
            // look for current horse
            if (this.lrst_stats.ContainsKey(st))
            {
                // look for its last race contender
                if (this.lrst_stats[st].ContainsKey(lrst))
                {
                    // get a copy of current stat we have
                    double w = this.lrst_stats[st][lrst].Item1;
                    double p = this.lrst_stats[st][lrst].Item2;

                    //we don't wanna overwrite real data with undefined data
                    if (win != Globals.UNDEFINED_RATE)
                        w = win;
                    if (place != Globals.UNDEFINED_RATE)
                        p = place;

                    // because tuple is read only, we need remove and add again
                    this.lrst_stats[st].Remove(lrst);
                    this.lrst_stats[st].Add(lrst, new Tuple<double, double>(w, p));
                }
                else
                {
                    // new contender
                    this.lrst_stats[st].Add(lrst, new Tuple<double, double>(win, place));
                }
            }
            else
            {
                // brand new entry
                this.lrst_stats.Add(st, new Dictionary<string, Tuple<double, double>> { { lrst, new Tuple<double, double>(win, place) } });
            }
        }

        private void ClearLastRaceStartersStats()
        {
            lrst_stats.Clear();
        }

        private double CalculateStamina(race_result entry)
        {
            double time = 0;
            long weight = entry.pounds;
            double distance = (double)entry.race_info.race_distance;
            if (Utils.GoingDescriptionBinaryConverter(entry.race_info.race_going) == 1)
                time = (double)entry.race_info.race_finishing_time + ((double)entry.distance_beaten * Globals.SEC_PER_LENGTH_GOOD);
            else
                time = (double)entry.race_info.race_finishing_time + ((double)entry.distance_beaten * Globals.SEC_PER_LENGTH_SLOW);

            //work done formula
            return (((2 * (weight + Globals.HORSE_AVG_WEIGHT) * distance) / (Math.Pow(time, 2))) * distance);
        }

        private double CalculateCurrentRacePredictedTime(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();

            double time = 0;
            double lrWeight = lr.pounds;
            double distance = (double)lr.race_info.race_distance;
            if (Utils.GoingDescriptionBinaryConverter(lr.race_info.race_going) == 1)
                time = (double)lr.race_info.race_finishing_time + ((double)lr.distance_beaten * Globals.SEC_PER_LENGTH_GOOD);
            else
                time = (double)lr.race_info.race_finishing_time + ((double)lr.distance_beaten * Globals.SEC_PER_LENGTH_SLOW);

            if (time == 0)
                return time;

            //exerted force on last race
            double xForce = ((((2 * (lrWeight + Globals.HORSE_AVG_WEIGHT) * distance) / (Math.Pow(time, 2))) * distance) / (double)this.distance);

            //now calculate the finishing time for this race
            return Math.Sqrt((2 * (st.pounds + Globals.HORSE_AVG_WEIGHT) * (double)this.distance) / xForce);
        }

        private int CalculatePredictedPace(starter_info st, List<starter_info> sts)
        {
            int fast = 0;
            int slow = 0;

            foreach (starter_info s in sts)
                if (CalculateCurrentRacePredictedTime(s) < CalculateCurrentRaceStandardTime()) fast++; else slow++;

            return (fast - slow);
        }

        private double CalculateCurrentRaceStandardTime()
        {
            double ret = 0;
            int surface = Utils.GetRaceSurface(this.going);
            int going = Utils.GoingDescriptionBinaryConverter(this.going);

            try
            {
                List<race_info> res = new List<race_info>(uow.RaceInfoRepository.Get(p => p.track_key == this.racetrack.Key)                //same racetrack
                                                                .Where(q => q.race_distance == this.distance)                               //same distance
                                                                .Where(r => Utils.GetRaceSurface(r.race_going) == surface)                  //same surface
                                                                .Where(s => Utils.GoingDescriptionBinaryConverter(s.race_going) == going)   //same going
                                                                .Where(t => t.race_class == this.race_class)                                //same class
                                                                .OrderByDescending(u => u.race_id)
                                                                .Take(Globals.MAX_RACETRACK_PREVIOUS_RACES));
                if (res.Count == 0)
                    ret = Globals.UNDEFINED_FINISHING_TIME;

                ret = ((double)res.Sum(x => x.race_finishing_time) / res.Count);
                res = null;

                return ret;
            }
            catch
            {
                return Globals.UNDEFINED_FINISHING_TIME;
            }
        }
        #endregion

        #region PV Calculations

        #region Category: PVCAT_THIS_RACE
        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_THIS_RACE, PredictorVariableDefinitions.PV_CR_LOG_ODDS)]
        private double PV_LogCrOdds(starter_info st)
        {
            return Math.Log(st.odds + 1);
        }
        #endregion

        #region Category: PVCAT_LAST_RACE
        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_LOG_ODDS)]
        private double PV_LogLastRaceOdds(starter_info st)
        {
            return Math.Log((double)st.previousRaces.FirstOrDefault().odds + 1);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_LOG_ODDS_OVER_RUNNERS)]
        private double PV_LogLastRaceOddsOverRunners(starter_info st)
        {
            return Math.Log((double)(((st.previousRaces.FirstOrDefault().odds + 1) / st.previousRaces.FirstOrDefault().race_info.race_number_of_runners) + 1));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_DOUBLE_LOG_ODDS)]
        private double PV_DoubleLogLastRaceOdds(starter_info st)
        {
            return Math.Log(PV_LogLastRaceOdds(st));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_DOUBLE_LOG_ODDS_OVER_RUNNERS)]
        private double PV_DoubleLogLastRaceOddsOverRunners(starter_info st)
        {
            return Math.Log(PV_LogLastRaceOddsOverRunners(st));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_LOG_CLASS_ADJ_ODDS)]
        private double PV_LogLastRaceOddsClassAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            return Math.Log((double)(lr.odds + 1) * ((double)lr.race_info.race_class / this.race_class));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_SIMPLE_DAYS)]
        private double PV_DaysSinceLastRace(starter_info st)
        {
            return Math.Abs((this.dt - st.previousRaces.FirstOrDefault().race_info.race_date).TotalDays);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_SIMPLE_F_POS)]
        private double PV_LastRaceFinishingPosition(starter_info st)
        {
            return st.previousRaces.FirstOrDefault().finishing_position;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_SIMPLE_DISTBEATEN)]
        private double PV_LastRaceDistanceBeaten(starter_info st)
        {
            return (double)st.previousRaces.FirstOrDefault().distance_beaten;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_ODDS_RANK)]
        private double PV_LastRaceOddsRank(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();

            List<race_result> lrstarters = new List<race_result>(lr.race_info.race_result.OrderBy(y => y.odds));
            int r = 1;
            double prev_odds = 0;
            foreach (race_result lrst in lrstarters)
            {
                if (lrst.horse_key == lr.horse_key)
                    break;

                if (prev_odds < (double)lrst.odds)
                {
                    prev_odds = (double)lrst.odds;
                    r++;
                }
            }

            return r;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_ODDS_RANK_FPOS_DIFF)]
        private double PV_LastRaceOddsRankFPosDiff(starter_info st)
        {
            return (this.PV_LastRaceOddsRank(st) - this.PV_LastRaceFinishingPosition(st));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_WEIGHT_ADJ_DISTBEATEN)]
        private double PV_LastRaceWeightAdjustedDistanceBeaten(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            double ppl = 0;

            if ((distance >= 5) && (distance < 6))          //5 furlongs
                ppl = Globals.POUND_PER_LENGTH_5F;
            else if ((distance >= 6) && (distance < 7))     //6 furlongs
                ppl = Globals.POUND_PER_LENGTH_6F;
            else if ((distance >= 7) && (distance < 9))     //7 to 8 furlongs
                ppl = Globals.POUND_PER_LENGTH_7TO8F;
            else if ((distance >= 9) && (distance < 11))    //9 to 10 furlongs   
                ppl = Globals.POUND_PER_LENGTH_9TO10F;
            else if ((distance >= 11) && (distance < 14))   //11 to 13 furlongs
                ppl = Globals.POUND_PER_LENGTH_11TO13F;
            else if ((distance >= 14) && (distance < 15))   //14 furlongs
                ppl = Globals.POUND_PER_LENGTH_14F;
            else if (distance >= 15)                        //15 and above furlongs
                ppl = Globals.POUND_PER_LENGTH_15F_UP;

            return ((double)lr.distance_beaten + (PV_LastRaceWeightDifference(st) / ppl) + ((Globals.RATING_RANGE_PER_CLASS * (lr.race_info.race_class - this.race_class)) / ppl));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_IS_FAVOURITE)]
        private double PV_LastRaceIsFavourite(starter_info st)
        {
            return ((bool)st.previousRaces.FirstOrDefault().is_favourite ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_DISTANCE_DIFF)]
        private double PV_LastRaceDistanceDifference(starter_info st)
        {
            return (double)(this.distance - st.previousRaces.FirstOrDefault().race_info.race_distance);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_WEIGHT_DIFF)]
        private double PV_LastRaceWeightDifference(starter_info st)
        {
            return (double)(st.pounds - st.previousRaces.FirstOrDefault().pounds);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_WEIGHT_RANK)]
        private double PV_LastRaceWeightRank(starter_info st)
        {

            race_result lr = st.previousRaces.FirstOrDefault();
            List<long> lrstarterswt = new List<long>(lr.race_info.race_result.Select(x=>x.pounds).Distinct());
            lrstarterswt.Sort();    // ascending
            lrstarterswt.Reverse(); //descending

            int r = 1;
            foreach (long lrstwt in lrstarterswt)
            {
                if (st.pounds == lrstwt)
                    break;

                r++;
            }

            return r;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_RUNNER_ADJ_POS)]
        private double PV_LastRaceFPosRunnerAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            return (((double)lr.finishing_position / lr.race_info.race_number_of_runners) * this.num_runners);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_PMON_ADJ_POS)]
        private double PV_LastRaceFPosPrizeMoneyAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            return (double)(((double)lr.finishing_position / (lr.race_info.race_prize_money + 1)) * this.prize_money);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_WINRATE_SUM_ADJ_POS)]
        private double PV_LastRaceFPosSumWinRateAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            race_info lr_info = lr.race_info;

            double sum = 0;
            foreach (race_result lrst in lr_info.race_result)
            {
                if (lrst.horse_info.horse_name.Equals(st.horse_name) == true)
                    continue;

                double wins = 0;
                double places = 0;
                Tuple<double, double> stat_get = GetLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name);
                if ((stat_get != null) && (stat_get.Item1 != Globals.UNDEFINED_RATE))
                {
                    wins = stat_get.Item1;
                }
                else
                {
                    List<race_result> pvrs = new List<race_result>(uow.HorseInfoRepository
                                .Get(x => x.horse_id == lrst.horse_key).FirstOrDefault().race_result
                                .Where(z => z.race_key < lrst.race_key)
                                .OrderByDescending(y => y.result_id)
                                .Take(Globals.MAX_STARTER_PREVIOUS_RACES));

                    wins = (double)pvrs.Count(x => x.is_winner == true) / pvrs.Count();
                    places = (double)pvrs.Count(x => x.is_placer == true) / pvrs.Count();
                    UpdateLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name, wins, places);
                }
                if (double.IsNaN(wins) == false)
                    sum = sum + wins;
            }

            return ((double)lr.finishing_position / (1 + sum));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_PLACERATE_SUM_ADJ_POS)]
        private double PV_LastRaceFPosSumPlaceRateAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            race_info lr_info = lr.race_info;

            double sum = 0;
            foreach (race_result lrst in lr_info.race_result)
            {
                if (lrst.horse_info.horse_name.Equals(st.horse_name) == true)
                    continue;

                double wins = 0;
                double places = 0;
                Tuple<double, double> stat_get = GetLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name);
                if ((stat_get != null) && (stat_get.Item2 != Globals.UNDEFINED_RATE))
                {
                    places = stat_get.Item2;
                }
                else
                {
                    List<race_result> pvrs = new List<race_result>(uow.HorseInfoRepository
                                .Get(x => x.horse_id == lrst.horse_key).FirstOrDefault().race_result
                                .Where(z => z.race_key < lrst.race_key)
                                .OrderByDescending(y => y.result_id)
                                .Take(Globals.MAX_STARTER_PREVIOUS_RACES));

                    wins = (double)pvrs.Count(x => x.is_winner == true) / pvrs.Count();
                    places = (double)pvrs.Count(x => x.is_placer == true) / pvrs.Count();
                    UpdateLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name, wins, places);
                }
                if (double.IsNaN(places) == false)
                    sum = sum + places;
            }

            return ((double)lr.finishing_position / (1 + sum));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_WINRATE_AVG_ADJ_POS)]
        private double PV_LastRaceFPosAvgWinRateAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            race_info lr_info = lr.race_info;

            double denom = 0;
            foreach (race_result lrst in lr_info.race_result)
            {
                if (lrst.horse_info.horse_name.Equals(st.horse_name) == true)
                    continue;

                double wins = 0;
                double places = 0;
                Tuple<double, double> stat_get = GetLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name);
                if ((stat_get != null) && (stat_get.Item1 != Globals.UNDEFINED_RATE))
                {
                    wins = stat_get.Item1;
                }
                else
                {
                    List<race_result> pvrs = new List<race_result>(uow.HorseInfoRepository
                                .Get(x => x.horse_id == lrst.horse_key).FirstOrDefault().race_result
                                .Where(z => z.race_key < lrst.race_key)
                                .OrderByDescending(y => y.result_id)
                                .Take(Globals.MAX_STARTER_PREVIOUS_RACES));

                    wins = (double)pvrs.Count(x => x.is_winner == true) / pvrs.Count();
                    places = (double)pvrs.Count(x => x.is_placer == true) / pvrs.Count();
                    UpdateLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name, wins, places);
                }
                if (double.IsNaN(wins) == false)
                    denom = denom + wins;
            }
            denom = denom / (lr_info.race_result.Count() - 1);

            return ((double)lr.finishing_position / (1 + denom));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_PLACERATE_AVG_ADJ_POS)]
        private double PV_LastRaceFPosAvgPlaceRateAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            race_info lr_info = lr.race_info;

            double denom = 0;
            foreach (race_result lrst in lr_info.race_result)
            {
                if (lrst.horse_info.horse_name.Equals(st.horse_name) == true)
                    continue;

                double wins = 0;
                double places = 0;
                Tuple<double, double> stat_get = GetLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name);
                if ((stat_get != null) && (stat_get.Item1 != Globals.UNDEFINED_RATE))
                {
                    places = stat_get.Item2;
                }
                else
                {
                    List<race_result> pvrs = new List<race_result>(uow.HorseInfoRepository
                                .Get(x => x.horse_id == lrst.horse_key).FirstOrDefault().race_result
                                .Where(z => z.race_key < lrst.race_key)
                                .OrderByDescending(y => y.result_id)
                                .Take(Globals.MAX_STARTER_PREVIOUS_RACES));

                    wins = (double)pvrs.Count(x => x.is_winner == true) / pvrs.Count();
                    places = (double)pvrs.Count(x => x.is_placer == true) / pvrs.Count();
                    UpdateLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name, wins, places);
                }
                if (double.IsNaN(places) == false)
                    denom = denom + places;
            }
            denom = denom / (lr_info.race_result.Count() - 1);

            return ((double)lr.finishing_position / (1 + denom));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_RUNNER_ADJ_DISB)]
        private double PV_LastRaceDisBRunnerAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            return (((double)lr.distance_beaten / lr.race_info.race_number_of_runners) * this.num_runners);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_PMON_ADJ_DISB)]
        private double PV_LastRaceDisBPrizeMoneyAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            return (double)(((double)lr.distance_beaten / (lr.race_info.race_prize_money + 1)) * this.prize_money);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_WINRATE_SUM_ADJ_DISB)]
        private double PV_LastRaceDisBSumWinRateAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            race_info lr_info = lr.race_info;

            double sum = 0;
            foreach (race_result lrst in lr_info.race_result)
            {
                if (lrst.horse_info.horse_name.Equals(st.horse_name) == true)
                    continue;

                double wins = 0;
                double places = 0;
                Tuple<double, double> stat_get = GetLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name);
                if ((stat_get != null) && (stat_get.Item1 != Globals.UNDEFINED_RATE))
                {
                    wins = stat_get.Item1;
                }
                else
                {
                    List<race_result> pvrs = new List<race_result>(uow.HorseInfoRepository
                                .Get(x => x.horse_id == lrst.horse_key).FirstOrDefault().race_result
                                .Where(z => z.race_key < lrst.race_key)
                                .OrderByDescending(y => y.result_id)
                                .Take(Globals.MAX_STARTER_PREVIOUS_RACES));

                    wins = (double)pvrs.Count(x => x.is_winner == true) / pvrs.Count();
                    places = (double)pvrs.Count(x => x.is_placer == true) / pvrs.Count();
                    UpdateLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name, wins, places);
                }
                if (double.IsNaN(wins) == false)
                    sum = sum + wins;
            }

            return ((double)lr.distance_beaten / (1 + sum));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_PLACERATE_SUM_ADJ_DISB)]
        private double PV_LastRaceDisBSumPlaceRateAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            race_info lr_info = lr.race_info;

            double sum = 0;
            foreach (race_result lrst in lr_info.race_result)
            {
                if (lrst.horse_info.horse_name.Equals(st.horse_name) == true)
                    continue;

                double wins = 0;
                double places = 0;
                Tuple<double, double> stat_get = GetLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name);
                if ((stat_get != null) && (stat_get.Item2 != Globals.UNDEFINED_RATE))
                {
                    places = stat_get.Item2;
                }
                else
                {
                    List<race_result> pvrs = new List<race_result>(uow.HorseInfoRepository
                                .Get(x => x.horse_id == lrst.horse_key).FirstOrDefault().race_result
                                .Where(z => z.race_key < lrst.race_key)
                                .OrderByDescending(y => y.result_id)
                                .Take(Globals.MAX_STARTER_PREVIOUS_RACES));

                    wins = (double)pvrs.Count(x => x.is_winner == true) / pvrs.Count();
                    places = (double)pvrs.Count(x => x.is_placer == true) / pvrs.Count();
                    UpdateLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name, wins, places);
                }
                if (double.IsNaN(places) == false)
                    sum = sum + places;
            }

            return ((double)lr.distance_beaten / (1 + sum));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_WINRATE_AVG_ADJ_DISB)]
        private double PV_LastRaceDisBAvgWinRateAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            race_info lr_info = lr.race_info;

            double denom = 0;
            foreach (race_result lrst in lr_info.race_result)
            {
                if (lrst.horse_info.horse_name.Equals(st.horse_name) == true)
                    continue;

                double wins = 0;
                double places = 0;
                Tuple<double, double> stat_get = GetLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name);
                if ((stat_get != null) && (stat_get.Item1 != Globals.UNDEFINED_RATE))
                {
                    wins = stat_get.Item1;
                }
                else
                {
                    List<race_result> pvrs = new List<race_result>(uow.HorseInfoRepository
                                .Get(x => x.horse_id == lrst.horse_key).FirstOrDefault().race_result
                                .Where(z => z.race_key < lrst.race_key)
                                .OrderByDescending(y => y.result_id)
                                .Take(Globals.MAX_STARTER_PREVIOUS_RACES));

                    wins = (double)pvrs.Count(x => x.is_winner == true) / pvrs.Count();
                    places = (double)pvrs.Count(x => x.is_placer == true) / pvrs.Count();
                    UpdateLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name, wins, places);
                }
                if (double.IsNaN(wins) == false)
                    denom = denom + wins;
            }
            denom = denom / (lr_info.race_result.Count() - 1);

            return ((double)lr.distance_beaten / (1 + denom));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_LAST_RACE, PredictorVariableDefinitions.PV_LR_PLACERATE_AVG_ADJ_DISB)]
        private double PV_LastRaceDisBAvgPlaceRateAdjusted(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            race_info lr_info = lr.race_info;

            double denom = 0;
            foreach (race_result lrst in lr_info.race_result)
            {
                if (lrst.horse_info.horse_name.Equals(st.horse_name) == true)
                    continue;

                double wins = 0;
                double places = 0;
                Tuple<double, double> stat_get = GetLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name);
                if ((stat_get != null) && (stat_get.Item1 != Globals.UNDEFINED_RATE))
                {
                    places = stat_get.Item2;
                }
                else
                {
                    List<race_result> pvrs = new List<race_result>(uow.HorseInfoRepository
                                .Get(x => x.horse_id == lrst.horse_key).FirstOrDefault().race_result
                                .Where(z => z.race_key < lrst.race_key)
                                .OrderByDescending(y => y.result_id)
                                .Take(Globals.MAX_STARTER_PREVIOUS_RACES));

                    wins = (double)pvrs.Count(x => x.is_winner == true) / pvrs.Count();
                    places = (double)pvrs.Count(x => x.is_placer == true) / pvrs.Count();
                    UpdateLastRaceStarterStats(st.horse_name, lrst.horse_info.horse_name, wins, places);
                }
                if (double.IsNaN(places) == false)
                    denom = denom + places;
            }
            denom = denom / (lr_info.race_result.Count() - 1);

            return ((double)lr.distance_beaten / (1 + denom));
        }
        #endregion

        #region Category: PVCAT_HORSE_GENENRAL
        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_DAYS_DIFF_FR_MEAN)]
        private double PV_HorseGenDaysDifferenceFromMean(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return 0;

            return (PV_DaysSinceLastRace(st) - ((double)(this.dt - lrs.LastOrDefault().race_info.race_date).TotalDays / lrs.Count));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_LOGABS_DAYDIF_DEV)]
        private double PV_HorseGenLogAbsDaysDifferenceDeviation(starter_info st)
        {
            double ret = PV_HorseGenDaysDifferenceFromMean(st);
            if (ret == 0)
                return ret;

            return Math.Log(Math.Abs(ret));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_AVERAGE_ODDS)]
        private double PV_HorseGenAvgOdds(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_ODDS;

            double odds = (double)lrs.Sum(x => (x.odds + 1));
            return (odds / lrs.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_LOG_AVERAGE_ODDS)]
        private double PV_HorseGenLogAvgOdds(starter_info st)
        {
            return Math.Log(PV_HorseGenAvgOdds(st));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_DOUBLE_LOG_AVERAGE_ODDS)]
        private double PV_HorseGenDoubleLogAvgOdds(starter_info st)
        {
            return Math.Log(PV_HorseGenLogAvgOdds(st));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_EXPOSURE_BY_AGE)]
        private double PV_HorseGenExposureByAge(starter_info st)
        {
            double race_count = (double)st.previousRaces.Count();
            double horse_age = (double)st.horse_age;

            return (race_count / horse_age);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_WIN_RATE)]
        private double PV_HorseGenWinRate(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double wins = lrs.Count(x => x.is_winner == true);
            return (wins / lrs.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_PLACE_RATE)]
        private double PV_HorseGenPlaceRate(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double places = lrs.Count(x => x.is_placer == true);
            return (places / lrs.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_AVG_F_POSITION)]
        private double PV_HorseGenAveageFinishingPosition(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)lrs.Sum(x => x.finishing_position) / lrs.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_AVG_DISTBEATEN)]
        private double PV_HorseGenAverageDistanceBeaten(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)lrs.Sum(x => x.distance_beaten) / lrs.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_RWT_F_POSITION)]
        private double PV_HorseGenRecencyWeightedFinishingPosition(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double sumpos = lrs.Select(i => i.finishing_position / ((this.dt - i.race_info.race_date).TotalDays)).Sum();
            double sumday = lrs.Select(i => 1 / ((this.dt - i.race_info.race_date).TotalDays)).Sum();

            return (sumpos / sumday);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_RWT_DISTBEATEN)]
        private double PV_HorseGenRecencyWeightedDistanceBeaten(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double sumdis = lrs.Select(i => (double)i.distance_beaten / ((this.dt - i.race_info.race_date).TotalDays)).Sum();
            double sumday = lrs.Select(i => 1 / ((this.dt - i.race_info.race_date).TotalDays)).Sum();

            return (sumdis / sumday);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_BEST_FPOS)]
        private double PV_HorseGenBestFinishingPosition(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)lrs.Min(x=>x.finishing_position);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_BEST_DISB)]
        private double PV_HorseGenBestDistanceBeaten(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)lrs.Min(x => x.distance_beaten);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_BEST_FPOS_DAYS)]
        private double PV_HorseGenBestFinishingPositionDays(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double best = (double)lrs.Min(x => x.finishing_position);
            DateTime bestdt = lrs.Where(x => (double)x.finishing_position == best).FirstOrDefault().race_info.race_date;
            TimeSpan span = bestdt.Subtract(this.dt);

            return (double)Math.Abs(span.TotalDays);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_BEST_DISB_DAYS)]
        private double PV_HorseGenBestDistanceBeatenDays(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double best = (double)lrs.Min(x => x.distance_beaten);
            DateTime bestdt = lrs.Where(x => (double)x.distance_beaten == best).FirstOrDefault().race_info.race_date;
            TimeSpan span = bestdt.Subtract(this.dt);

            return (double)Math.Abs(span.TotalDays);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_WORST_FPOS)]
        private double PV_HorseGenWorstFinishingPosition(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)lrs.Max(x => x.finishing_position);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_WORST_DISB)]
        private double PV_HorseGenWorstDistanceBeaten(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)lrs.Max(x => x.distance_beaten);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_WORST_FPOS_DAYS)]
        private double PV_HorseGenWorstFinishingPositionDays(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double worst = (double)lrs.Max(x => x.finishing_position);
            DateTime worstdt = lrs.Where(x => (double)x.finishing_position == worst).FirstOrDefault().race_info.race_date;
            TimeSpan span = worstdt.Subtract(this.dt);

            return (double)Math.Abs(span.TotalDays);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_WORST_DISB_DAYS)]
        private double PV_HorseGenWorstDistanceBeatenDays(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double worst = (double)lrs.Max(x => x.distance_beaten);
            DateTime worstdt = lrs.Where(x => (double)x.distance_beaten == worst).FirstOrDefault().race_info.race_date;
            TimeSpan span = worstdt.Subtract(this.dt);

            return (double)Math.Abs(span.TotalDays);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_BEST2WORST_FPOS_DAYS)]
        private double PV_HorseGenBestToWorstFinishingPositionDays(starter_info st)
        {
            return (double)(PV_HorseGenWorstFinishingPositionDays(st) - PV_HorseGenBestFinishingPositionDays(st));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_BEST2WORST_DISB_DAYS)]
        private double PV_HorseGenBestToWorstDistanceBeatenDays(starter_info st)
        {
            return (double)(PV_HorseGenWorstDistanceBeatenDays(st) - PV_HorseGenBestDistanceBeatenDays(st));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_LOG_UPCLASSAGE)]
        private double PV_HorseGenLogUpClassByAge(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return 0;

            int classDiff = lrs.FirstOrDefault().race_info.race_class - this.race_class;
            if (classDiff > 0)
                return Math.Log(classDiff * st.horse_age);
            else
                return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_AVG_STAMINA)]
        private double PV_HorseGenAverageStamina(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return 0;

            List<race_result> entries = new List<race_result>(lrs.Where(x => x.race_info.race_finishing_time > (decimal)Globals.UNDEFINED_FINISHING_TIME));
            if (entries.Count == 0)
                return 0;

            double stamina = 0;
            foreach (race_result entry in entries)
            {
                stamina = stamina + CalculateStamina(entry);
            }

            return (stamina / entries.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_FPOS_LR_IMPROV_SCR)]
        private double PV_HorseGenLastRaceFPosImproveScore(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count > 1)
            {
                double total = 0;
                for (int i = 1; i < 2; i++)
                {
                    double score = ((double)((lrs[i].finishing_position * lrs[i].race_info.race_class) - (lrs[i - 1].finishing_position * lrs[i - 1].race_info.race_class)) / lrs[i - 1].race_info.race_class);
                    total = total + score;
                }

                return total;
            }
            else
            {
                return 0;
            }
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_DISB_LR_IMPROV_SCR)]
        private double PV_HorseGenLastRaceDisBImproveScore(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count > 1)
            {
                double total = 0;
                for (int i = 1; i < 2; i++)
                {
                    double score = ((((double)lrs[i].distance_beaten * lrs[i].race_info.race_class) - ((double)lrs[i - 1].distance_beaten * lrs[i - 1].race_info.race_class)) / lrs[i - 1].race_info.race_class);
                    total = total + score;
                }

                return total;
            }
            else
            {
                return 0;
            }
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_FPOS_ALL_IMPROV_SCR)]
        private double PV_HorseGenAllRacesFPosImproveScore(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count > 1)
            {
                double total = 0;
                for (int i = 1; i < lrs.Count; i++)
                {
                    double score = ((double)((lrs[i].finishing_position * lrs[i].race_info.race_class) - (lrs[i - 1].finishing_position * lrs[i - 1].race_info.race_class)) / lrs[i - 1].race_info.race_class);
                    total = total + score;
                }

                return total;
            }
            else
            {
                return 0;
            }
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_DISB_ALL_IMPROV_SCR)]
        private double PV_HorseGenAllRacesDisBImproveScore(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count > 1)
            {
                double total = 0;
                for (int i = 1; i < lrs.Count; i++)
                {
                    double score = ((((double)lrs[i].distance_beaten * lrs[i].race_info.race_class) - ((double)lrs[i - 1].distance_beaten * lrs[i - 1].race_info.race_class)) / lrs[i - 1].race_info.race_class);
                    total = total + score;
                }

                return total;
            }
            else
            {
                return 0;
            }
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_TREND_F_POSITION)]
        private double PV_HorseGenTrendFPosition(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count <= 1)
                return 0;

            List<double> ydata = new List<double>(lrs.Select(x => (double)x.finishing_position).Reverse());
            List<double> xdata = new List<double>();
            for (int i = 0; i < ydata.Count(); i++) { xdata.Add((double)(i + 1)); }

            Tuple<double, double> p = Fit.Line(xdata.ToArray(), ydata.ToArray());

            return p.Item2; // slope
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_TREND_DISTBEATEN)]
        private double PV_HorseGenTrendDistBeaten(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count <= 1)
                return 0;

            List<double> ydata = new List<double>(lrs.Select(x => (double)x.distance_beaten).Reverse());
            List<double> xdata = new List<double>();
            for (int i = 0; i < ydata.Count(); i++) { xdata.Add((double)(i + 1)); }

            Tuple<double, double> p = Fit.Line(xdata.ToArray(), ydata.ToArray());

            return p.Item2; // slope
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_TREND_ODDS)]
        private double PV_HorseGenTrendOdds(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count <= 1)
                return 0;

            List<double> ydata = new List<double>(lrs.Select(x => (double)(x.odds / x.race_info.race_number_of_runners)));
            List<double> xdata = new List<double>();
            for (int i = 0; i < ydata.Count(); i++) { xdata.Add((double)(i + 1)); }

            Tuple<double, double> p = Fit.Line(xdata.ToArray(), ydata.ToArray());

            return p.Item2; // slope
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_TREND_WEIGHT)]
        private double PV_HorseGenTrendWeight(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count <= 1)
                return 0;

            List<double> ydata = new List<double>(lrs.Select(x => (double)x.pounds));
            List<double> xdata = new List<double>();
            for (int i = 0; i < ydata.Count(); i++) { xdata.Add((double)(i + 1)); }

            Tuple<double, double> p = Fit.Line(xdata.ToArray(), ydata.ToArray());

            return p.Item2; // slope
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_LEVEL_STAKE_GAIN)]
        private double PV_HorseGenLevelStakeGain(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;

            return ((double)lrs.Where(x => x.is_winner == true).Select(y => y.odds).Sum() - lrs.Count(z => z.is_winner == false));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_GENENRAL, PredictorVariableDefinitions.PV_HGEN_LEVEL_STAKE_GAIN_AVERAGE)]
        private double PV_HorseGenLevelStakeGainAverage(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;

            return ((double)((lrs.Where(x => x.is_winner == true).Select(y => y.odds).Sum() - lrs.Count(z => z.is_winner == false)) / lrs.Count()));
        }
        #endregion

        #region Category: PVCAT_HORSE_BY_RACE_TYPE
        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_CLAS_AVG_F_POS)]
        private double PV_ByRaceTypeAverageFPosByClass(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => x.race_info.race_class == this.race_class));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.finishing_position) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_CLAS_AVG_DISBEATEN)]
        private double PV_ByRaceTypeAverageDisBByClass(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => x.race_info.race_class == this.race_class));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.distance_beaten) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_CLAS_RWT_F_POS)]
        private double PV_ByRaceTypeRecencyWeightedFPosByClass(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => x.race_info.race_class == this.race_class));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double sumpos = res.Select(i => i.finishing_position / ((this.dt - i.race_info.race_date).TotalDays)).Sum();
            double sumday = res.Select(i => 1 / ((this.dt - i.race_info.race_date).TotalDays)).Sum();

            return (sumpos / sumday);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_CLAS_RWT_DISBEATEN)]
        private double PV_ByRaceTypeRecencyWeightedDisBByClass(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => x.race_info.race_class == this.race_class));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double sumdis = res.Select(i => (double)i.distance_beaten / ((this.dt - i.race_info.race_date).TotalDays)).Sum();
            double sumday = res.Select(i => 1 / ((this.dt - i.race_info.race_date).TotalDays)).Sum();

            return (sumdis / sumday);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_CLAS_WIN_PERCENTAGE)]
        private double PV_ByRaceTypeWinPercentageByClass(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => x.race_info.race_class == this.race_class));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double wins = res.Count(x => x.is_winner == true);
            return (wins / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_CLAS_PLACE_PERCENTAGE)]
        private double PV_ByRaceTypePlacePercentageByClass(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => x.race_info.race_class == this.race_class));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double places = res.Count(x => x.is_placer == true);
            return (places / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_DIST_AVG_F_POS)]
        private double PV_ByRaceTypeAverageFPosByDistance(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => Math.Abs(x.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.finishing_position) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_DIST_AVG_DISBEATEN)]
        private double PV_ByRaceTypeAverageDisBByDistance(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => Math.Abs(x.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.distance_beaten) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_DIST_WIN_PERCENTAGE)]
        private double PV_ByRaceTypeWinPercentageByDistance(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => Math.Abs(x.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double wins = res.Count(x => x.is_winner == true);
            return (wins / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_DIST_PLACE_PERCENTAGE)]
        private double PV_ByRaceTypePlacePercentageByDistance(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => Math.Abs(x.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double places = res.Count(x => x.is_placer == true);
            return (places / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_GOIN_AVG_F_POS)]
        private double PV_ByRaceTypeAverageFPosByGoing(starter_info st)
        {
            int going = Utils.GoingDescriptionBinaryConverter(this.going);
            List<race_result> res = new List<race_result>();
            foreach (race_result r in st.previousRaces)
            {
                if (Utils.GoingDescriptionBinaryConverter(r.race_info.race_going) == going)
                    res.Add(r);
            }

            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.finishing_position) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_GOIN_AVG_DISBEATEN)]
        private double PV_ByRaceTypeAverageDisBByGoing(starter_info st)
        {
            int going = Utils.GoingDescriptionBinaryConverter(this.going);
            List<race_result> res = new List<race_result>();
            foreach (race_result r in st.previousRaces)
            {
                if (Utils.GoingDescriptionBinaryConverter(r.race_info.race_going) == going)
                    res.Add(r);
            }

            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.distance_beaten) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_GOIN_WIN_PERCENTAGE)]
        private double PV_ByRaceTypeWinPercentageByGoing(starter_info st)
        {
            int going = Utils.GoingDescriptionBinaryConverter(this.going);
            List<race_result> res = new List<race_result>();
            foreach (race_result r in st.previousRaces)
            {
                if (Utils.GoingDescriptionBinaryConverter(r.race_info.race_going) == going)
                    res.Add(r);
            }

            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double wins = res.Count(x => x.is_winner == true);
            return (wins / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_GOIN_PLACE_PERCENTAGE)]
        private double PV_ByRaceTypePlacePercentageByGoing(starter_info st)
        {
            int going = Utils.GoingDescriptionBinaryConverter(this.going);
            List<race_result> res = new List<race_result>();
            foreach (race_result r in st.previousRaces)
            {
                if (Utils.GoingDescriptionBinaryConverter(r.race_info.race_going) == going)
                    res.Add(r);
            }

            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double places = res.Count(x => x.is_placer == true);
            return (places / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_TYPE_AVG_F_POS)]
        private double PV_ByRaceTypeAverageFPosByRaceType(starter_info st)
        {
            string rtype = "";

            if (this.race_name.Contains(Globals.RACE_TYPE_HANDICAP))
                rtype = Globals.RACE_TYPE_HANDICAP;
            if (this.race_name.Contains(Globals.RACE_TYPE_NURSERY))
                rtype = Globals.RACE_TYPE_NURSERY;
            if (this.race_name.Contains(Globals.RACE_TYPE_MAIDEN))
                rtype = Globals.RACE_TYPE_MAIDEN;
            if (this.race_name.Contains(Globals.RACE_TYPE_NOVICE))
                rtype = Globals.RACE_TYPE_NOVICE;
            if (this.race_name.Contains(Globals.RACE_TYPE_CONDITIONS_STAKES))
                rtype = Globals.RACE_TYPE_CONDITIONS_STAKES;
            if (this.race_name.Contains(Globals.RACE_TYPE_CLASSIFIED_STAKES))
                rtype = Globals.RACE_TYPE_CLASSIFIED_STAKES;
            if (this.race_name.Contains(Globals.RACE_TYPE_NOVICE_AUCTION))
                rtype = Globals.RACE_TYPE_NOVICE_AUCTION;
            if (this.race_name.Contains(Globals.RACE_TYPE_MAIDEN_HANDICAP))
                rtype = Globals.RACE_TYPE_MAIDEN_HANDICAP;
            if (this.race_name.Contains(Globals.RACE_TYPE_CLAIMING_STAKES))
                rtype = Globals.RACE_TYPE_CLAIMING_STAKES;
            if (this.race_name.Contains(Globals.RACE_TYPE_SELLING_STAKES))
                rtype = Globals.RACE_TYPE_SELLING_STAKES;

            List<race_result> res = new List<race_result>();
            foreach (race_result r in st.previousRaces)
            {
                if (Utils.RaceTypeComparator(rtype, this.race_name) == true)
                    res.Add(r);
            }

            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.finishing_position) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_TYPE_AVG_DISBEATEN)]
        private double PV_ByRaceTypeAverageDisBByRaceType(starter_info st)
        {
            string rtype = "";

            if (this.race_name.Contains(Globals.RACE_TYPE_HANDICAP))
                rtype = Globals.RACE_TYPE_HANDICAP;
            if (this.race_name.Contains(Globals.RACE_TYPE_NURSERY))
                rtype = Globals.RACE_TYPE_NURSERY;
            if (this.race_name.Contains(Globals.RACE_TYPE_MAIDEN))
                rtype = Globals.RACE_TYPE_MAIDEN;
            if (this.race_name.Contains(Globals.RACE_TYPE_NOVICE))
                rtype = Globals.RACE_TYPE_NOVICE;
            if (this.race_name.Contains(Globals.RACE_TYPE_CONDITIONS_STAKES))
                rtype = Globals.RACE_TYPE_CONDITIONS_STAKES;
            if (this.race_name.Contains(Globals.RACE_TYPE_CLASSIFIED_STAKES))
                rtype = Globals.RACE_TYPE_CLASSIFIED_STAKES;
            if (this.race_name.Contains(Globals.RACE_TYPE_NOVICE_AUCTION))
                rtype = Globals.RACE_TYPE_NOVICE_AUCTION;
            if (this.race_name.Contains(Globals.RACE_TYPE_MAIDEN_HANDICAP))
                rtype = Globals.RACE_TYPE_MAIDEN_HANDICAP;
            if (this.race_name.Contains(Globals.RACE_TYPE_CLAIMING_STAKES))
                rtype = Globals.RACE_TYPE_CLAIMING_STAKES;
            if (this.race_name.Contains(Globals.RACE_TYPE_SELLING_STAKES))
                rtype = Globals.RACE_TYPE_SELLING_STAKES;

            List<race_result> res = new List<race_result>();
            foreach (race_result r in st.previousRaces)
            {
                if (Utils.RaceTypeComparator(rtype, this.race_name) == true)
                    res.Add(r);
            }

            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.distance_beaten) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_TYPE_WIN_PERCENTAGE)]
        private double PV_ByRaceTypeWinPercentageByRaceType(starter_info st)
        {
            string rtype = "";

            if (this.race_name.Contains(Globals.RACE_TYPE_HANDICAP))
                rtype = Globals.RACE_TYPE_HANDICAP;
            if (this.race_name.Contains(Globals.RACE_TYPE_NURSERY))
                rtype = Globals.RACE_TYPE_NURSERY;
            if (this.race_name.Contains(Globals.RACE_TYPE_MAIDEN))
                rtype = Globals.RACE_TYPE_MAIDEN;
            if (this.race_name.Contains(Globals.RACE_TYPE_NOVICE))
                rtype = Globals.RACE_TYPE_NOVICE;
            if (this.race_name.Contains(Globals.RACE_TYPE_CONDITIONS_STAKES))
                rtype = Globals.RACE_TYPE_CONDITIONS_STAKES;
            if (this.race_name.Contains(Globals.RACE_TYPE_CLASSIFIED_STAKES))
                rtype = Globals.RACE_TYPE_CLASSIFIED_STAKES;
            if (this.race_name.Contains(Globals.RACE_TYPE_NOVICE_AUCTION))
                rtype = Globals.RACE_TYPE_NOVICE_AUCTION;
            if (this.race_name.Contains(Globals.RACE_TYPE_MAIDEN_HANDICAP))
                rtype = Globals.RACE_TYPE_MAIDEN_HANDICAP;
            if (this.race_name.Contains(Globals.RACE_TYPE_CLAIMING_STAKES))
                rtype = Globals.RACE_TYPE_CLAIMING_STAKES;
            if (this.race_name.Contains(Globals.RACE_TYPE_SELLING_STAKES))
                rtype = Globals.RACE_TYPE_SELLING_STAKES;

            List<race_result> res = new List<race_result>();
            foreach (race_result r in st.previousRaces)
            {
                if (Utils.RaceTypeComparator(rtype, this.race_name) == true)
                    res.Add(r);
            }

            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double wins = res.Count(x => x.is_winner == true);
            return (wins / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_TYPE_PLACE_PERCENTAGE)]
        private double PV_ByRaceTypePlacePercentageByRaceType(starter_info st)
        {
            string rtype = "";

            if (this.race_name.Contains(Globals.RACE_TYPE_HANDICAP))
                rtype = Globals.RACE_TYPE_HANDICAP;
            if (this.race_name.Contains(Globals.RACE_TYPE_NURSERY))
                rtype = Globals.RACE_TYPE_NURSERY;
            if (this.race_name.Contains(Globals.RACE_TYPE_MAIDEN))
                rtype = Globals.RACE_TYPE_MAIDEN;
            if (this.race_name.Contains(Globals.RACE_TYPE_NOVICE))
                rtype = Globals.RACE_TYPE_NOVICE;
            if (this.race_name.Contains(Globals.RACE_TYPE_CONDITIONS_STAKES))
                rtype = Globals.RACE_TYPE_CONDITIONS_STAKES;
            if (this.race_name.Contains(Globals.RACE_TYPE_CLASSIFIED_STAKES))
                rtype = Globals.RACE_TYPE_CLASSIFIED_STAKES;
            if (this.race_name.Contains(Globals.RACE_TYPE_NOVICE_AUCTION))
                rtype = Globals.RACE_TYPE_NOVICE_AUCTION;
            if (this.race_name.Contains(Globals.RACE_TYPE_MAIDEN_HANDICAP))
                rtype = Globals.RACE_TYPE_MAIDEN_HANDICAP;
            if (this.race_name.Contains(Globals.RACE_TYPE_CLAIMING_STAKES))
                rtype = Globals.RACE_TYPE_CLAIMING_STAKES;
            if (this.race_name.Contains(Globals.RACE_TYPE_SELLING_STAKES))
                rtype = Globals.RACE_TYPE_SELLING_STAKES;

            List<race_result> res = new List<race_result>();
            foreach (race_result r in st.previousRaces)
            {
                if (Utils.RaceTypeComparator(rtype, this.race_name) == true)
                    res.Add(r);
            }

            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double places = res.Count(x => x.is_placer == true);
            return (places / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_AVG_F_POS)]
        private double PV_ByRaceTypeAverageFPosByCourse(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => x.race_info.track_key == this.racetrack.Key));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.finishing_position) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_AVG_DISBEATEN)]
        private double PV_ByRaceTypeAverageDisBByCourse(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => x.race_info.track_key == this.racetrack.Key));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.distance_beaten) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_WIN_PERCENTAGE)]
        private double PV_ByRaceTypeWinPercentageByCourse(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => x.race_info.track_key == this.racetrack.Key));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double wins = res.Count(x => x.is_winner == true);
            return (wins / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_PLACE_PERCENTAGE)]
        private double PV_ByRaceTypePlacePercentageByCourse(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => x.race_info.track_key == this.racetrack.Key));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double places = res.Count(x => x.is_placer == true);
            return (places / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_CHAR_AVG_F_POS)]
        private double PV_ByRaceTypeAverageFPosByCourseCharacteristic(starter_info st)
        {
            string course_characteristic = uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().flat_characteristic;

            List<race_result> res = new List<race_result>(st.previousRaces
                .Where(x => x.race_info.racetrack.flat_characteristic.Equals(course_characteristic) == true));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.finishing_position) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_CHAR_AVG_DISBEATEN)]
        private double PV_ByRaceTypeAverageDisBByCourseCharacteristic(starter_info st)
        {
            string course_characteristic = uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().flat_characteristic;

            List<race_result> res = new List<race_result>(st.previousRaces
                .Where(x => x.race_info.racetrack.flat_characteristic.Equals(course_characteristic) == true));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.distance_beaten) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_CHAR_WIN_PERCENTAGE)]
        private double PV_ByRaceTypeWinPercentageByCourseCharacteristic(starter_info st)
        {
            string course_characteristic = uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().flat_characteristic;

            List<race_result> res = new List<race_result>(st.previousRaces
                .Where(x => x.race_info.racetrack.flat_characteristic.Equals(course_characteristic) == true));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double wins = res.Count(x => x.is_winner == true);
            return (wins / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_CHAR_PLACE_PERCENTAGE)]
        private double PV_ByRaceTypePlacePercentageByCourseCharacteristic(starter_info st)
        {
            string course_characteristic = uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().flat_characteristic;

            List<race_result> res = new List<race_result>(st.previousRaces
                .Where(x => x.race_info.racetrack.flat_characteristic.Equals(course_characteristic) == true));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double places = res.Count(x => x.is_placer == true);
            return (places / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_TURN_AVG_F_POS)]
        private double PV_ByRaceTypeAverageFPosByCourseTurnDirection(starter_info st)
        {
            double straight_dist = (double)uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().straight_distance;
            List<race_result> res = new List<race_result>();

            //on straight course
            if (straight_dist >= (double)this.distance)
            {
                res.AddRange(st.previousRaces.Where(x => x.race_info.racetrack.straight_distance >= this.distance));
            }
            else
            {
                string turn_direction = uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().turn_direction;
                res.AddRange(st.previousRaces.Where(x => x.race_info.racetrack.turn_direction.Equals(turn_direction) == true));
            }

            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.finishing_position) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_TURN_AVG_DISBEATEN)]
        private double PV_ByRaceTypeAverageDisBByCourseTurnDirection(starter_info st)
        {
            double straight_dist = (double)uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().straight_distance;
            List<race_result> res = new List<race_result>();

            //on straight course
            if (straight_dist >= (double)this.distance)
            {
                res.AddRange(st.previousRaces.Where(x => x.race_info.racetrack.straight_distance >= this.distance));
            }
            else
            {
                string turn_direction = uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().turn_direction;
                res.AddRange(st.previousRaces.Where(x => x.race_info.racetrack.turn_direction.Equals(turn_direction) == true));
            }

            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.distance_beaten) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_TURN_WIN_PERCENTAGE)]
        private double PV_ByRaceTypeWinPercentageByCourseTurnDirection(starter_info st)
        {
            double straight_dist = (double)uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().straight_distance;
            List<race_result> res = new List<race_result>();

            //on straight course
            if (straight_dist >= (double)this.distance)
            {
                res.AddRange(st.previousRaces.Where(x => x.race_info.racetrack.straight_distance >= this.distance));
            }
            else
            {
                string turn_direction = uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().turn_direction;
                res.AddRange(st.previousRaces.Where(x => x.race_info.racetrack.turn_direction.Equals(turn_direction) == true));
            }

            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double wins = res.Count(x => x.is_winner == true);
            return (wins / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_TURN_PLACE_PERCENTAGE)]
        private double PV_ByRaceTypePlacePercentageByCourseTurnDirection(starter_info st)
        {
            double straight_dist = (double)uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().straight_distance;
            List<race_result> res = new List<race_result>();

            //on straight course
            if (straight_dist >= (double)this.distance)
            {
                res.AddRange(st.previousRaces.Where(x => x.race_info.racetrack.straight_distance >= this.distance));
            }
            else
            {
                string turn_direction = uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().turn_direction;
                res.AddRange(st.previousRaces.Where(x => x.race_info.racetrack.turn_direction.Equals(turn_direction) == true));
            }

            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double places = res.Count(x => x.is_placer == true);
            return (places / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_SPEED_AVG_F_POS)]
        private double PV_ByRaceTypeAverageFPosByCourseSpeed(starter_info st)
        {
            string course_speed = uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().speed;

            List<race_result> res = new List<race_result>(st.previousRaces
                .Where(x => x.race_info.racetrack.speed.Equals(course_speed) == true));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.finishing_position) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_SPEED_AVG_DISBEATEN)]
        private double PV_ByRaceTypeAverageDisBByCourseSpeed(starter_info st)
        {
            string course_speed = uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().speed;

            List<race_result> res = new List<race_result>(st.previousRaces
                .Where(x => x.race_info.racetrack.speed.Equals(course_speed) == true));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(y => y.distance_beaten) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_SPEED_WIN_PERCENTAGE)]
        private double PV_ByRaceTypeWinPercentageByCourseSpeed(starter_info st)
        {
            string course_speed = uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().speed;

            List<race_result> res = new List<race_result>(st.previousRaces
                .Where(x => x.race_info.racetrack.speed.Equals(course_speed) == true));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double wins = res.Count(x => x.is_winner == true);
            return (wins / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_BY_RACE_TYPE, PredictorVariableDefinitions.PV_COUR_SPEED_PLACE_PERCENTAGE)]
        private double PV_ByRaceTypePlacePercentageByCourseSpeed(starter_info st)
        {
            string course_speed = uow.RaceTrackRepository.Get(x => x.track_id.Equals(this.racetrack.Key)).FirstOrDefault().speed;

            List<race_result> res = new List<race_result>(st.previousRaces
                .Where(x => x.race_info.racetrack.speed.Equals(course_speed) == true));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double places = res.Count(x => x.is_placer == true);
            return (places / res.Count);
        }
        #endregion

        #region Category: PVCAT_HORSE_DUMMY_CODES
        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LAST_TIME_OUT_WINNER)]
        private double PV_DummyCodesLastTimeOutWinner(starter_info st)
        {
            return (st.previousRaces.FirstOrDefault().is_winner == true ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_2_CONSERCUTIVE_WINNER)]
        private double PV_DummyCodesTwoConsercutiveWin(starter_info st)
        {
            List<race_result> twolrs = new List<race_result>(st.previousRaces.Take(2));
            foreach (race_result lr in twolrs)
            {
                if (lr.is_winner == false)
                    return 0;
            }

            return 1;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_3_CONSERCUTIVE_WINNER)]
        private double PV_DummyCodesThreeConsercutiveWin(starter_info st)
        {
            List<race_result> threelrs = new List<race_result>(st.previousRaces.Take(3));
            foreach (race_result lr in threelrs)
            {
                if (lr.is_winner == false)
                    return 0;
            }

            return 1;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_4_CONSERCUTIVE_WINNER)]
        private double PV_DummyCodesFourConsercutiveWin(starter_info st)
        {
            List<race_result> fourlrs = new List<race_result>(st.previousRaces.Take(4));
            foreach (race_result lr in fourlrs)
            {
                if (lr.is_winner == false)
                    return 0;
            }

            return 1;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LAST_TIME_OUT_PLACER)]
        private double PV_DummyCodesLastTimeOutPlacer(starter_info st)
        {
            return (st.previousRaces.FirstOrDefault().is_placer == true ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_2_CONSERCUTIVE_PLACER)]
        private double PV_DummyCodesTwoConsercutivePlace(starter_info st)
        {
            List<race_result> twolrs = new List<race_result>(st.previousRaces.Take(2));
            foreach (race_result lr in twolrs)
            {
                if (lr.is_placer == false)
                    return 0;
            }

            return 1;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_3_CONSERCUTIVE_PLACER)]
        private double PV_DummyCodesThreeConsercutivePlace(starter_info st)
        {
            List<race_result> threelrs = new List<race_result>(st.previousRaces.Take(3));
            foreach (race_result lr in threelrs)
            {
                if (lr.is_placer == false)
                    return 0;
            }

            return 1;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_4_CONSERCUTIVE_PLACER)]
        private double PV_DummyCodesFourConsercutivePlace(starter_info st)
        {
            List<race_result> fourlrs = new List<race_result>(st.previousRaces.Take(4));
            foreach (race_result lr in fourlrs)
            {
                if (lr.is_placer == false)
                    return 0;
            }

            return 1;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_ODDS_ON_WINNER)]
        private double PV_DummyCodesOddsOnWinner(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if ((lr.is_winner == true) && (lr.odds < 1))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_ODDS_ON_PLACER)]
        private double PV_DummyCodesOddsOnPlacer(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if ((lr.is_placer == true) && (lr.odds < 1))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_1ST_FAVOURITE_WINNER)]
        private double PV_DummyCodesFirstFavouriteWinner(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if ((lr.is_winner == true) && (lr.is_favourite == true))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_1ST_FAVOURITE_PLACER)]
        private double PV_DummyCodesFirstFavouritePlacer(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if ((lr.is_placer == true) && (lr.is_favourite == true))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_2ND_FAVOURITE_WINNER)]
        private double PV_DummyCodesSecondFavouriteWinner(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if (lr.is_winner == false)
                return 0;

            List<race_result> lrstarters = new List<race_result>(lr.race_info.race_result.Where(x => x.is_favourite != true).OrderBy(y => y.odds));
            if (lrstarters.Count() == 0)
                return 0;

            return (lr.horse_key == lrstarters.First().horse_key ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_2ND_FAVOURITE_PLACER)]
        private double PV_DummyCodesSecondFavouritePlacer(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if (lr.is_placer == false)
                return 0;

            List<race_result> lrstarters = new List<race_result>(lr.race_info.race_result.Where(x => x.is_favourite != true).OrderBy(y => y.odds));
            if (lrstarters.Count() == 0)
                return 0;

            return (lr.horse_key == lrstarters.First().horse_key ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_10TO1_UP_WINNER)]
        private double PV_DummyCodesTenToOneAboveWinner(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if ((lr.is_winner == true) && (lr.odds > 10))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_10TO1_UP_PLACER)]
        private double PV_DummyCodesTenToOneAbovePlacer(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if ((lr.is_placer == true) && (lr.odds > 10))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_20TO1_UP_WINNER)]
        private double PV_DummyCodesTwentyToOneAboveWinner(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if ((lr.is_winner == true) && (lr.odds > 20))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_20TO1_UP_PLACER)]
        private double PV_DummyCodesTwentyToOneAbovePlacer(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if ((lr.is_placer == true) && (lr.odds > 20))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_TOP_WEIGHT_WINNER)]
        private double PV_DummyCodesLrTopWeightWinner(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if (lr.is_winner != true)
                return 0;

            double w = lr.race_info.race_result.Max(x => x.pounds);

            return (lr.pounds >= w ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_TOP_WEIGHT_PLACER)]
        private double PV_DummyCodesLrTopWeightPlacer(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if (lr.is_placer != true)
                return 0;

            double w = lr.race_info.race_result.Max(x => x.pounds);

            return (lr.pounds >= w ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_BOTTOM_WEIGHT_WINNER)]
        private double PV_DummyCodesLrBottomWeightWinner(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if (lr.is_winner != true)
                return 0;

            double w = lr.race_info.race_result.Min(x => x.pounds);

            return (lr.pounds <= w ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_BOTTOM_WEIGHT_PLACER)]
        private double PV_DummyCodesLrBottomWeightPlacer(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if (lr.is_placer != true)
                return 0;

            double w = lr.race_info.race_result.Min(x => x.pounds);

            return (lr.pounds <= w ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_BEATEN_LESS_THAN_1LENGTH)]
        private double PV_DummyCodesLrBeatenLessThan1(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            return ((double)(lr.distance_beaten < 1 ? 1 : 0));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_BEATEN_LESS_THAN_2LENGTH)]
        private double PV_DummyCodesLrBeatenLessThan2(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            return ((double)(lr.distance_beaten < 2 ? 1 : 0));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_BEATEN_MORE_THAN_5LENGTH)]
        private double PV_DummyCodesLrBeatenMoreThan5(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            return ((double)(lr.distance_beaten > 5 ? 1 : 0));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_LR_BEATEN_MORE_THAN_10LENGTH)]
        private double PV_DummyCodesLrBeatenMoreThan10(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            return ((double)(lr.distance_beaten > 10 ? 1 : 0));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_IMPROVING_F_POSITION)]
        private double PV_DummyCodesIsImprovingFPosition(starter_info st)
        {
            return (this.PV_HorseGenTrendFPosition(st) > 0 ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_IMPROVING_DISTBEATEN)]
        private double PV_DummyCodesIsImprovingDistBeaten(starter_info st)
        {
            return (this.PV_HorseGenTrendDistBeaten(st) > 0 ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_SHORTENING_ODDS)]
        private double PV_DummyCodesIsShorteningOdds(starter_info st)
        {
            return (this.PV_HorseGenTrendOdds(st) > 0 ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_DROPPING_WEIGHT)]
        private double PV_DummyCodesIsDroppingWeight(starter_info st)
        {
            return (this.PV_HorseGenTrendWeight(st) > 0 ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_CONVERGED_ODDS_FPOS)]
        private double PV_DummyCodesIsConvergedOddsFPos(starter_info st)
        {
            if ((this.PV_HorseGenTrendOdds(st) < 0) && (this.PV_HorseGenTrendFPosition(st) > 0))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_DIVERGED_ODDS_FPOS)]
        private double PV_DummyCodesIsDivergedOddsFPos(starter_info st)
        {
            if ((this.PV_HorseGenTrendOdds(st) > 0) && (this.PV_HorseGenTrendFPosition(st) < 0))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_PARALLEL_ODDS_FPOS)]
        private double PV_DummyCodesIsParallelOddsFPos(starter_info st)
        {
            if (((this.PV_HorseGenTrendOdds(st) > 0) && (this.PV_HorseGenTrendFPosition(st) > 0)) ||
                ((this.PV_HorseGenTrendOdds(st) < 0) && (this.PV_HorseGenTrendFPosition(st) < 0)))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_CONVERGED_ODDS_DISB)]
        private double PV_DummyCodesIsConvergedOddsDisB(starter_info st)
        {
            if ((this.PV_HorseGenTrendOdds(st) < 0) && (this.PV_HorseGenTrendDistBeaten(st) > 0))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_DIVERGED_ODDS_DISB)]
        private double PV_DummyCodesIsDivergedOddsDisB(starter_info st)
        {
            if ((this.PV_HorseGenTrendOdds(st) > 0) && (this.PV_HorseGenTrendDistBeaten(st) < 0))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_PARALLEL_ODDS_DISB)]
        private double PV_DummyCodesIsParallelOddsDisB(starter_info st)
        {
            if (((this.PV_HorseGenTrendOdds(st) > 0) && (this.PV_HorseGenTrendDistBeaten(st) > 0)) ||
                ((this.PV_HorseGenTrendOdds(st) < 0) && (this.PV_HorseGenTrendDistBeaten(st) < 0)))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_CONVERGED_WEIGHT_FPOS)]
        private double PV_DummyCodesIsConvergedWeightFPos(starter_info st)
        {
            if ((this.PV_HorseGenTrendWeight(st) < 0) && (this.PV_HorseGenTrendFPosition(st) > 0))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_DIVERGED_WEIGHT_FPOS)]
        private double PV_DummyCodesIsDivergedWeightFPos(starter_info st)
        {
            if ((this.PV_HorseGenTrendWeight(st) > 0) && (this.PV_HorseGenTrendFPosition(st) < 0))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_PARALLEL_WEIGHT_FPOS)]
        private double PV_DummyCodesIsParallelWeightFPos(starter_info st)
        {
            if (((this.PV_HorseGenTrendWeight(st) > 0) && (this.PV_HorseGenTrendFPosition(st) > 0)) ||
                ((this.PV_HorseGenTrendWeight(st) < 0) && (this.PV_HorseGenTrendFPosition(st) < 0)))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_CONVERGED_WEIGHT_DISB)]
        private double PV_DummyCodesIsConvergedWeightDisB(starter_info st)
        {
            if ((this.PV_HorseGenTrendWeight(st) < 0) && (this.PV_HorseGenTrendDistBeaten(st) > 0))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_DIVERGED_WEIGHT_DISB)]
        private double PV_DummyCodesIsDivergedWeightDisB(starter_info st)
        {
            if ((this.PV_HorseGenTrendWeight(st) > 0) && (this.PV_HorseGenTrendDistBeaten(st) < 0))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_IS_PARALLEL_WEIGHT_DISB)]
        private double PV_DummyCodesIsParallelWeightDisB(starter_info st)
        {
            if (((this.PV_HorseGenTrendWeight(st) > 0) && (this.PV_HorseGenTrendDistBeaten(st) > 0)) ||
                ((this.PV_HorseGenTrendWeight(st) < 0) && (this.PV_HorseGenTrendDistBeaten(st) < 0)))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_DISTANCE_WINNER)]
        private double PV_DummyCodesDistanceWinner(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => Math.Abs(x.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE));
            if (res.Count == 0)
                return 0;

            return (res.Any(y => y.is_winner == true) == true ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_DISTANCE_PLACER)]
        private double PV_DummyCodesDistancePlacer(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => Math.Abs(x.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE));
            if (res.Count == 0)
                return 0;

            return (res.Any(y => y.is_placer == true) == true ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_COURSE_WINNER)]
        private double PV_DummyCodesCourseWinner(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => x.race_info.track_key == this.racetrack.Key));
            if (res.Count == 0)
                return 0;

            return (res.Any(y => y.is_winner == true) == true ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_COURSE_PLACER)]
        private double PV_DummyCodesCoursePlacer(starter_info st)
        {
            List<race_result> res = new List<race_result>(st.previousRaces.Where(x => x.race_info.track_key == this.racetrack.Key));
            if (res.Count == 0)
                return 0;

            return (res.Any(y => y.is_placer == true) == true ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UPCLASS_WINNER)]
        private double PV_DummyCodesUpClassWinner(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count > 1)
            {
                double ret = 0;
                for (int i = 1; i < 2; i++)
                {
                    if (lrs[i].race_info.race_class > lrs[i - 1].race_info.race_class)
                    {
                        if (lrs[i - 1].is_winner == true)
                        {
                            ret = 1;
                            break;
                        }
                    }
                }

                return ret;
            }
            else
            {
                return 0;
            }
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UPCLASS_PLACER)]
        private double PV_DummyCodesUpClassPlacer(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count > 1)
            {
                double ret = 0;
                for (int i = 1; i < 2; i++)
                {
                    if (lrs[i].race_info.race_class > lrs[i - 1].race_info.race_class)
                    {
                        if (lrs[i - 1].is_placer == true)
                        {
                            ret = 1;
                            break;
                        }
                    }
                }

                return ret;
            }
            else
            {
                return 0;
            }
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_DWCLASS_WINNER)]
        private double PV_DummyCodesDownClassWinner(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count > 1)
            {
                double ret = 0;
                for (int i = 1; i < 2; i++)
                {
                    if (lrs[i].race_info.race_class < lrs[i - 1].race_info.race_class)
                    {
                        if (lrs[i - 1].is_winner == true)
                        {
                            ret = 1;
                            break;
                        }
                    }
                }

                return ret;
            }
            else
            {
                return 0;
            }
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_DWCLASS_PLACER)]
        private double PV_DummyCodesDownClassPlacer(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count > 1)
            {
                double ret = 0;
                for (int i = 1; i < 2; i++)
                {
                    if (lrs[i].race_info.race_class < lrs[i - 1].race_info.race_class)
                    {
                        if (lrs[i - 1].is_placer == true)
                        {
                            ret = 1;
                            break;
                        }
                    }
                }

                return ret;
            }
            else
            {
                return 0;
            }
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UPPRIZE_WINNER)]
        private double PV_DummyCodesUpPrizeWinner(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if (lr == null)
                return 0;

            if ((lr.is_winner == true) && (this.prize_money > lr.race_info.race_prize_money))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UPPRIZE_PLACER)]
        private double PV_DummyCodesUpPrizePlacer(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if (lr == null)
                return 0;

            if ((lr.is_placer == true) && (this.prize_money > lr.race_info.race_prize_money))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_DWPRIZE_WINNER)]
        private double PV_DummyCodesDownPrizeWinner(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if (lr == null)
                return 0;

            if ((lr.is_winner == true) && (this.prize_money < lr.race_info.race_prize_money))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_DWPRIZE_PLACER)]
        private double PV_DummyCodesDownPrizePlacer(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            if (lr == null)
                return 0;

            if ((lr.is_placer == true) && (this.prize_money < lr.race_info.race_prize_money))
                return 1;

            return 0;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UP_DISTANCE)]
        private double PV_DummyCodesUpDistance(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return 0;

            double ret = 0;
            if (this.distance > lrs.FirstOrDefault().race_info.race_distance)
                ret = 1;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UP_WEIGHT)]
        private double PV_DummyCodesUpWeight(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return 0;

            double ret = 0;
            if (st.pounds > lrs.FirstOrDefault().pounds)
                ret = 1;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UP_CLASS)]
        private double PV_DummyCodesUpClass(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            if (lrs.Count == 0)
                return 0;

            double ret = 0;
            if (this.race_class < lrs.FirstOrDefault().race_info.race_class)
                ret = 1;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UP_DW_COMBO)]
        private double PV_DummyCodesUpDistanceWeightCombo(starter_info st)
        {
            double ret = 0;
            if (PV_DummyCodesUpDistance(st) != 0 && PV_DummyCodesUpWeight(st) != 0)
                ret = 1;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UP_WC_COMBO)]
        private double PV_DummyCodesUpWeightClassCombo(starter_info st)
        {
            double ret = 0;
            if (PV_DummyCodesUpWeight(st) != 0 && PV_DummyCodesUpClass(st) != 0)
                ret = 1;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UP_DC_COMBO)]
        private double PV_DummyCodesUpDistanceClassCombo(starter_info st)
        {
            double ret = 0;
            if (PV_DummyCodesUpDistance(st) != 0 && PV_DummyCodesUpClass(st) != 0)
                ret = 1;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UP_2CHALLENGES)]
        private double PV_DummyCodesUpTwoChallenges(starter_info st)
        {
            return (double)((int)PV_DummyCodesUpDistanceWeightCombo(st) | (int)PV_DummyCodesUpWeightClassCombo(st) | (int)PV_DummyCodesUpDistanceClassCombo(st));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UP_3CHALLENGES)]
        private double PV_DummyCodesUpThreeChallenges(starter_info st)
        {
            return ((int)PV_DummyCodesUpDistance(st) & (int)PV_DummyCodesUpWeight(st) & (int)PV_DummyCodesUpClass(st));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_WEIGHT_UP_5POUNDS)]
        private double PV_DummyCodesUpFivePoundsPlus(starter_info st)
        {
            return ((st.pounds - st.previousRaces.FirstOrDefault().pounds) > 5 ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_WEIGHT_UP_10POUNDS)]
        private double PV_DummyCodesUpTenPoundsPlus(starter_info st)
        {
            return ((st.pounds - st.previousRaces.FirstOrDefault().pounds) > 10 ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_WEIGHT_DOWN_5POUNDS)]
        private double PV_DummyCodesDownFivePoundsPlus(starter_info st)
        {
            return ((st.previousRaces.FirstOrDefault().pounds - st.pounds) > 5 ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_WEIGHT_DOWN_10POUNDS)]
        private double PV_DummyCodesDownTenPoundsPlus(starter_info st)
        {
            return ((st.previousRaces.FirstOrDefault().pounds - st.pounds) > 10 ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UNEXP_DISTANCE)]
        private double PV_DummyCodesUnexposedDistance(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            return (lrs.Any(x => Math.Abs(x.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE) == false ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UNEXP_CLASS)]
        private double PV_DummyCodesUnexposedClass(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            return (lrs.Any(x => x.race_info.race_class == this.race_class) == false ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_UNEXP_COURSE)]
        private double PV_DummyCodesUnexposedCourse(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            return (lrs.Any(x => x.race_info.track_key == this.racetrack.Key) == false ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_HIGHEST_WEIGHT)]
        private double PV_DummyCodesHighestWeight(starter_info st)
        {
            List<race_result> lrs = st.previousRaces;
            return (lrs.Any(x => x.pounds > st.pounds) == false ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_JOCKEY_CHANGED)]
        private double PV_DummyCodesJockeyChanged(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            return (lr.jockey_key != st.jockey_name.Key ? 1 : 0);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_HORSE_DUMMY_CODES, PredictorVariableDefinitions.PV_DCODE_TRAINER_CHANGED)]
        private double PV_DummyCodesTrainerChanged(starter_info st)
        {
            race_result lr = st.previousRaces.FirstOrDefault();
            return (lr.trainer_key != st.trainer_name.Key ? 1 : 0);
        }
        #endregion

        #region Category: PVCAT_STALL
        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_STALL, PredictorVariableDefinitions.PV_STALL_AVG_F_POS)]
        private double PV_StallAverageFinishingPosition(starter_info st)
        {
            int surface = Utils.GetRaceSurface(this.going);
            int going = Utils.GoingDescriptionBinaryConverter(this.going);
            List<race_result> res = new List<race_result>(uow.RaceResultRepository.Get(p => p.race_info.track_key == this.racetrack.Key)    //same racetrack
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)               //previous race only
                                                            .Where(q => q.stall == st.stall)                                                  //same stall
                                                            .Where(r => Utils.GetRaceSurface(r.race_info.race_going) == surface)              //same surface
                                                            .Where(s => Utils.GoingDescriptionBinaryConverter(s.race_info.race_going) == going)     //same going
                                                            .OrderByDescending(t => t.result_id)
                                                            .Take(Globals.MAX_STARTER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(x => x.finishing_position) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_STALL, PredictorVariableDefinitions.PV_STALL_RUN_ADJ_AVG_F_POS)]
        private double PV_StallAverageFPosRunnerAdjusted(starter_info st)
        {
            int surface = Utils.GetRaceSurface(this.going);
            int going = Utils.GoingDescriptionBinaryConverter(this.going);
            List<race_result> res = new List<race_result>(uow.RaceResultRepository.Get(p => p.race_info.track_key == this.racetrack.Key)    //same racetrack
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(q => q.stall == st.stall)                                                  //same stall
                                                            .Where(r => Utils.GetRaceSurface(r.race_info.race_going) == surface)              //same surface
                                                            .Where(s => Utils.GoingDescriptionBinaryConverter(s.race_info.race_going) == going)     //same going
                                                            .OrderByDescending(t => t.result_id)
                                                            .Take(Globals.MAX_STARTER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double sumpos = res.Select(x => (double)x.finishing_position / x.race_info.race_number_of_runners).Sum();
            return (sumpos / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_STALL, PredictorVariableDefinitions.PV_STALL_AVG_DISBEATEN)]
        private double PV_StallAverageDistanceBeaten(starter_info st)
        {
            int surface = Utils.GetRaceSurface(this.going);
            int going = Utils.GoingDescriptionBinaryConverter(this.going);
            List<race_result> res = new List<race_result>(uow.RaceResultRepository.Get(p => p.race_info.track_key == this.racetrack.Key)    //same racetrack
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(q => q.stall == st.stall)                                                  //same stall
                                                            .Where(r => Utils.GetRaceSurface(r.race_info.race_going) == surface)              //same surface
                                                            .Where(s => Utils.GoingDescriptionBinaryConverter(s.race_info.race_going) == going)     //same going
                                                            .OrderByDescending(t => t.result_id)
                                                            .Take(Globals.MAX_STARTER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            return (double)res.Sum(x => x.distance_beaten) / res.Count;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_STALL, PredictorVariableDefinitions.PV_STALL_WIN_PERCENTAGE)]
        private double PV_StallWinPercentage(starter_info st)
        {
            int surface = Utils.GetRaceSurface(this.going);
            int going = Utils.GoingDescriptionBinaryConverter(this.going);
            List<race_result> res = new List<race_result>(uow.RaceResultRepository.Get(p => p.race_info.track_key == this.racetrack.Key)    //same racetrack
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(q => q.stall == st.stall)                                                  //same stall
                                                            .Where(r => Utils.GetRaceSurface(r.race_info.race_going) == surface)              //same surface
                                                            .Where(s => Utils.GoingDescriptionBinaryConverter(s.race_info.race_going) == going)     //same going
                                                            .OrderByDescending(t => t.result_id)
                                                            .Take(Globals.MAX_STARTER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double wins = res.Count(x => x.is_winner == true);
            return (wins / res.Count);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_STALL, PredictorVariableDefinitions.PV_STALL_PLACE_PERCENTAGE)]
        private double PV_StallPlacePercentage(starter_info st)
        {
            int surface = Utils.GetRaceSurface(this.going);
            int going = Utils.GoingDescriptionBinaryConverter(this.going);
            List<race_result> res = new List<race_result>(uow.RaceResultRepository.Get(p => p.race_info.track_key == this.racetrack.Key)    //same racetrack
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(q => q.stall == st.stall)                                                  //same stall
                                                            .Where(r => Utils.GetRaceSurface(r.race_info.race_going) == surface)              //same surface
                                                            .Where(s => Utils.GoingDescriptionBinaryConverter(s.race_info.race_going) == going)     //same going
                                                            .OrderByDescending(t => t.result_id)
                                                            .Take(Globals.MAX_STARTER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double places = res.Count(x => x.is_placer == true);
            return (places / res.Count);
        }
        #endregion

        #region Category: PVCAT_JOCKEY
        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOCKEY_AVG_F_POS)]
        private double PV_JockeyAverageFinishingPosition(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                    .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                    .OrderByDescending(y => y.result_id)
                                    .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.finishing_position) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOCKEY_RUN_ADJ_AVG_F_POS)]
        private double PV_JockeyAverageFPosRunnerAdjusted(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                    .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                    .OrderByDescending(y => y.result_id)
                                    .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double sumpos = res.Select(x => (double)x.finishing_position / x.race_info.race_number_of_runners).Sum();
            ret = (sumpos / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOCKEY_AVG_DISBEATEN)]
        private double PV_JockeyAverageDistanceBeaten(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                    .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                    .OrderByDescending(y => y.result_id)
                                    .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.distance_beaten) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOCKEY_WIN_PERCENTAGE)]
        private double PV_JockeyWinPercentage(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                    .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                    .OrderByDescending(y => y.result_id)
                                    .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double wins = res.Count(x => x.is_winner == true);
            ret = (wins / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOCKEY_PLACE_PERCENTAGE)]
        private double PV_JockeyPlacePercentage(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                    .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                    .OrderByDescending(y => y.result_id)
                                    .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double places = res.Count(x => x.is_placer == true);
            ret = (places / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOC_DIST_AVG_F_POS)]
        private double PV_JockeyAverageFinishingPositionByDistance(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => Math.Abs(y.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.finishing_position) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOC_DIST_RUN_ADJ_AVG_F_POS)]
        private double PV_JockeyAverageFPosRunnerAdjustedByDistance(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => Math.Abs(y.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double sumpos = res.Select(x => (double)x.finishing_position / x.race_info.race_number_of_runners).Sum();
            ret = (sumpos / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOC_DIST_AVG_DISBEATEN)]
        private double PV_JockeyAverageDistanceBeatenByDistance(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => Math.Abs(y.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.distance_beaten) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOC_DIST_WIN_PERCENTAGE)]
        private double PV_JockeyWinPercentageByDistance(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => Math.Abs(y.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double wins = res.Count(x => x.is_winner == true);
            ret = (wins / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOC_DIST_PLACE_PERCENTAGE)]
        private double PV_JockeyPlacePercentageByDistance(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => Math.Abs(y.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double places = res.Count(x => x.is_placer == true);
            ret = (places / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOC_COUR_AVG_F_POS)]
        private double PV_JockeyAverageFinishingPositionByCourse(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.race_info.track_key == this.racetrack.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.finishing_position) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOC_COUR_RUN_ADJ_AVG_F_POS)]
        private double PV_JockeyAverageFPosRunnerAdjustedByCourse(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.race_info.track_key == this.racetrack.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double sumpos = res.Select(x => (double)x.finishing_position / x.race_info.race_number_of_runners).Sum();
            ret = (sumpos / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOC_COUR_AVG_DISBEATEN)]
        private double PV_JockeyAverageDistanceBeatenByCourse(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.race_info.track_key == this.racetrack.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.distance_beaten) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOC_COUR_WIN_PERCENTAGE)]
        private double PV_JockeyWinPercentageByCourse(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.race_info.track_key == this.racetrack.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double wins = res.Count(x => x.is_winner == true);
            ret = (wins / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_JOCKEY, PredictorVariableDefinitions.PV_JOC_COUR_PLACE_PERCENTAGE)]
        private double PV_JockeyPlacePercentageByCourse(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.JockeyInfoRepository.Get(x => x.jockey_id == st.jockey_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.race_info.track_key == this.racetrack.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JOCKEY_PREVIOUS_RACES));
            if (res.Count() == 0)
                return Globals.UNDEFINED_FINISHING;

            double places = res.Count(x => x.is_placer == true);
            ret = (places / res.Count());
            res = null;

            return ret;
        }
        #endregion

        #region Category: PVCAT_TRAINER
        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRAINER_AVG_F_POS)]
        private double PV_TrainerAverageFinishingPosition(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                    .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                    .OrderByDescending(y => y.result_id)
                                    .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.finishing_position) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRAINER_RUN_ADJ_AVG_F_POS)]
        private double PV_TrainerAverageFPosRunnerAdjusted(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                    .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                    .OrderByDescending(y => y.result_id)
                                    .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double sumpos = res.Select(x => (double)x.finishing_position / x.race_info.race_number_of_runners).Sum();
            ret = (sumpos / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRAINER_AVG_DISBEATEN)]
        private double PV_TrainerAverageDistanceBeaten(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                    .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                    .OrderByDescending(y => y.result_id)
                                    .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.distance_beaten) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRAINER_WIN_PERCENTAGE)]
        private double PV_TrainerWinPercentage(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                    .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                    .OrderByDescending(y => y.result_id)
                                    .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double wins = res.Count(x => x.is_winner == true);
            ret = (wins / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRAINER_PLACE_PERCENTAGE)]
        private double PV_TrainerPlacePercentage(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                    .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                    .OrderByDescending(y => y.result_id)
                                    .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double places = res.Count(x => x.is_placer == true);
            ret = (places / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_DIST_AVG_F_POS)]
        private double PV_TrainerAverageFinishingPositionByDistance(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => Math.Abs(y.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.finishing_position) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_DIST_RUN_ADJ_AVG_F_POS)]
        private double PV_TrainerAverageFPosRunnerAdjustedByDistance(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => Math.Abs(y.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double sumpos = res.Select(x => (double)x.finishing_position / x.race_info.race_number_of_runners).Sum();
            ret = (sumpos / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_DIST_AVG_DISBEATEN)]
        private double PV_TrainerAverageDistanceBeatenByDistance(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => Math.Abs(y.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.distance_beaten) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_DIST_WIN_PERCENTAGE)]
        private double PV_TrainerWinPercentageByDistance(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => Math.Abs(y.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double wins = res.Count(x => x.is_winner == true);
            ret = (wins / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_DIST_PLACE_PERCENTAGE)]
        private double PV_TrainerPlacePercentageByDistance(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => Math.Abs(y.race_info.race_distance - this.distance) < Globals.RACE_DISTANCE_TOLERANCE)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double places = res.Count(x => x.is_placer == true);
            ret = (places / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_COUR_AVG_F_POS)]
        private double PV_TrainerAverageFinishingPositionByCourse(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.race_info.track_key == this.racetrack.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.finishing_position) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_COUR_RUN_ADJ_AVG_F_POS)]
        private double PV_TrainerAverageFPosRunnerAdjustedByCourse(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.race_info.track_key == this.racetrack.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double sumpos = res.Select(x => (double)x.finishing_position / x.race_info.race_number_of_runners).Sum();
            ret = (sumpos / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_COUR_AVG_DISBEATEN)]
        private double PV_TrainerAverageDistanceBeatenByCourse(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.race_info.track_key == this.racetrack.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.distance_beaten) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_COUR_WIN_PERCENTAGE)]
        private double PV_TrainerWinPercentageByCourse(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.race_info.track_key == this.racetrack.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double wins = res.Count(x => x.is_winner == true);
            ret = (wins / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_COUR_PLACE_PERCENTAGE)]
        private double PV_TrainerPlacePercentageByCourse(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.race_info.track_key == this.racetrack.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double places = res.Count(x => x.is_placer == true);
            ret = (places / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_HAGE_AVG_F_POS)]
        private double PV_TrainerAverageFinishingPositionByHorseAge(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.horse_age == st.horse_age)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.finishing_position) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_HAGE_RUN_ADJ_AVG_F_POS)]
        private double PV_TrainerAverageFPosRunnerAdjustedByHorseAge(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.horse_age == st.horse_age)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double sumpos = res.Select(x => (double)x.finishing_position / x.race_info.race_number_of_runners).Sum();
            ret = (sumpos / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_HAGE_AVG_DISBEATEN)]
        private double PV_TrainerAverageDistanceBeatenByHorseAge(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.horse_age == st.horse_age)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.distance_beaten) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_HAGE_WIN_PERCENTAGE)]
        private double PV_TrainerWinPercentageByHorseAge(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.horse_age == st.horse_age)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double wins = res.Count(x => x.is_winner == true);
            ret = (wins / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_TRA_HAGE_PLACE_PERCENTAGE)]
        private double PV_TrainerPlacePercentageByHorseAge(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.horse_age == st.horse_age)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_TRAINER_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double places = res.Count(x => x.is_placer == true);
            ret = (places / res.Count);
            res = null;

            return ret;
        }
        #endregion

        #region Category: PVCAT_JT_RELATIONSHIP
        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_JTRSHIP_AVG_F_POS)]
        private double PV_JTRelationshipAverageFinishingPosition(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.jockey_key == st.jockey_name.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JTRSHIP_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.finishing_position) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_JTRSHIP_RUN_ADJ_AVG_F_POS)]
        private double PV_JTRelationshipAverageFPosRunnerAdjusted(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.jockey_key == st.jockey_name.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JTRSHIP_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            double sumpos = res.Select(x => (double)x.finishing_position / x.race_info.race_number_of_runners).Sum();
            ret = (sumpos / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_JTRSHIP_AVG_DISBEATEN)]
        private double PV_JTRelationshipAverageDistanceBeaten(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.jockey_key == st.jockey_name.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JTRSHIP_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_FINISHING;

            ret = (double)res.Sum(x => x.distance_beaten) / res.Count;
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_JTRSHIP_WIN_PERCENTAGE)]
        private double PV_JTRelationshipWinPercentage(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.jockey_key == st.jockey_name.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JTRSHIP_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double wins = res.Count(x => x.is_winner == true);
            ret = (wins / res.Count);
            res = null;

            return ret;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_TRAINER, PredictorVariableDefinitions.PV_JTRSHIP_PLACE_PERCENTAGE)]
        private double PV_JTRelationshipPlacePercentage(starter_info st)
        {
            double ret = 0;
            List<race_result> res = new List<race_result>(uow.TrainerInfoRepository.Get(x => x.trainer_id == st.trainer_name.Key).FirstOrDefault().race_result
                                                            .Where(i => DateTime.Compare(i.race_info.race_date, this.dt) < 0)              //previous race only
                                                            .Where(y => y.jockey_key == st.jockey_name.Key)
                                                            .OrderByDescending(z => z.result_id)
                                                            .Take(Globals.MAX_JTRSHIP_PREVIOUS_RACES));
            if (res.Count == 0)
                return Globals.UNDEFINED_PERCENTAGE;

            double places = res.Count(x => x.is_placer == true);
            ret = (places / res.Count);
            res = null;

            return ret;
        }
        #endregion

        #region Category: PVCAT_RACE_COMPETITIVENESS
        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_NUM_WON_AGAINST)]
        private double PV_CompetitivenessWonAgainst(starter_info st, List<starter_info> sts)
        {
            double winCount = 0;
            if (st.previousRaces.Count == 0)
                return winCount;

            List<string> competitorList = new List<string>(sts.Select(x => x.horse_name));
            List<string> ignoreList = new List<string>();
            //yourself is not your competitor, ignore it
            ignoreList.Add(st.horse_name);

            //check all previous races one by one
            foreach (race_result race in st.previousRaces)
            {
                List<string> compareList = competitorList.Except(ignoreList).ToList();
                //find competitors one by one
                List<race_result> entries = new List<race_result>(uow.RaceResultRepository.Get(y => y.race_key == race.race_key));
                foreach (string comp in compareList)
                {
                    race_result compResult = entries.Where(y => y.horse_info.horse_name.CompareTo(comp) == 0).FirstOrDefault();
                    if (compResult != null)
                    {
                        //check if it had better result than this competitor
                        if (race.finishing_position < compResult.finishing_position)
                        {
                            winCount = winCount + 1;
                            //found head to head won against, ignore this competitor in the race iteration
                            ignoreList.Add(comp);
                        }
                    }
                }
                entries = null;
            }

            return winCount;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_NUM_LOST_TO)]
        private double PV_CompetitivenessLostTo(starter_info st, List<starter_info> sts)
        {
            double lostCount = 0;
            if (st.previousRaces.Count == 0)
                return lostCount;

            List<string> competitorList = new List<string>(sts.Select(x => x.horse_name));
            List<string> ignoreList = new List<string>();
            //yourself is not your competitor, ignore it
            ignoreList.Add(st.horse_name);

            //check all previous races one by one
            foreach (race_result race in st.previousRaces)
            {
                List<string> compareList = competitorList.Except(ignoreList).ToList();
                //find competitors one by one
                List<race_result> entries = new List<race_result>(uow.RaceResultRepository.Get(y => y.race_key == race.race_key));
                foreach (string comp in compareList)
                {
                    race_result compResult = entries.Where(y => y.horse_info.horse_name.CompareTo(comp) == 0).FirstOrDefault();
                    if (compResult != null)
                    {
                        //check if it had better result than this competitor
                        if (race.finishing_position > compResult.finishing_position)
                        {
                            lostCount = lostCount + 1;
                            //found head to head won against, ignore this competitor in the race iteration
                            ignoreList.Add(comp);
                        }
                    }
                }
                entries = null;
            }

            return lostCount;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_HOR_EXPOSURE_BY_AGE)]
        private double PV_CompetitivenessExposureByAge(starter_info st, List<starter_info> sts)
        {
            double exp = 0;
            foreach (starter_info s in sts)
                exp = exp + PV_HorseGenExposureByAge(s);

            double avgexp = exp / sts.Count();

            return (double)(PV_HorseGenExposureByAge(st) - avgexp);
        }


        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_HOR_WEIGHTDIST_REL)]
        private double PV_CompetitivenessHorseWeightDistance(starter_info st, List<starter_info> sts)
        {
            double avgWeight = sts.Sum(x => x.pounds) / sts.Count;
            return ((st.pounds - avgWeight) * (double)this.distance);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_HOR_LR_WEIGHTRANK_DIFF)]
        private double PV_CompetitivenessHorseLrWeightRankDifference(starter_info st, List<starter_info> sts)
        {
            List<int> stswt = new List<int>(sts.Select(x=>x.pounds).Distinct());
            stswt.Sort();   // asceding
            stswt.Reverse();//descending

            int wr = 1;
            foreach (int s in stswt)
            {
                if ((int)st.pounds == s)
                    break;

                wr++;
            }

            return ((double)(this.PV_LastRaceWeightRank(st) - wr));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_HOR_WIN_RATE_REL)]
        private double PV_CompetitivenessHorseWinRate(starter_info st, List<starter_info> sts)
        {
            double avgWinRate = sts.Sum(x => PV_HorseGenWinRate(x)) / sts.Count;
            return (PV_HorseGenWinRate(st) - avgWinRate);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_HOR_PLACE_RATE_REL)]
        private double PV_CompetitivenessHorsePlaceRate(starter_info st, List<starter_info> sts)
        {
            double avgPlaceRate = sts.Sum(x => PV_HorseGenPlaceRate(x)) / sts.Count;
            return (PV_HorseGenPlaceRate(st) - avgPlaceRate);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_HOR_AVG_F_POS_REL)]
        private double PV_CompetitivenessHorseAverageFPos(starter_info st, List<starter_info> sts)
        {
            double avgFinishingPosition = sts.Sum(x => PV_HorseGenAveageFinishingPosition(x)) / sts.Count;
            return (PV_HorseGenAveageFinishingPosition(st) - avgFinishingPosition);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_HOR_AVG_DISTBEATEN_REL)]
        private double PV_CompetitivenessHorseDisB(starter_info st, List<starter_info> sts)
        {
            double avgDistanceBeaten = sts.Sum(x => PV_HorseGenAverageDistanceBeaten(x)) / sts.Count;
            return (PV_HorseGenAverageDistanceBeaten(st) - avgDistanceBeaten);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_HOR_STAMINA_DIFF)]
        private double PV_CompetitivenessHorseStaminaDifference(starter_info st, List<starter_info> sts)
        {
            double avgStamina = sts.Sum(x => PV_HorseGenAverageStamina(x)) / sts.Count;
            return (PV_HorseGenAverageStamina(st) - avgStamina);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_HOR_PACE_ADVANTAGE)]
        private double PV_CompetitivenessHorsePaceAdvantage(starter_info st, List<starter_info> sts)
        {
            double paceAdvantage = 0;
            int predictedPace = CalculatePredictedPace(st, sts);

            //fast pace
            if ((predictedPace > 0) && (PV_CompetitivenessHorseStdTimeDiff(st, sts) < 0))
                paceAdvantage = 1;
            //slow pace
            else if ((predictedPace < 0) && (PV_CompetitivenessHorseStdTimeDiff(st, sts) > 0))
                paceAdvantage = 1;
            else
                paceAdvantage = 0;

            return paceAdvantage;
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_HOR_F_STD_TIME_DIFF)]
        private double PV_CompetitivenessHorseStdTimeDiff(starter_info st, List<starter_info> sts)
        {
            return (CalculateCurrentRaceStandardTime() - CalculateCurrentRacePredictedTime(st));
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_HOR_F_AVG_TIME_DIFF)]
        private double PV_CompetitivenessHorseAvgTimeDiff(starter_info st, List<starter_info> sts)
        {
            double avgTime = sts.Sum(x => CalculateCurrentRacePredictedTime(x)) / sts.Count;
            return (CalculateCurrentRacePredictedTime(st) - avgTime);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_JOC_WIN_RATE_REL)]
        private double PV_CompetitivenessJockeyWinRate(starter_info st, List<starter_info> sts)
        {
            double avgWinRate = sts.Sum(x => PV_JockeyWinPercentage(x)) / sts.Count;
            return (PV_JockeyWinPercentage(st) - avgWinRate);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_JOC_PLACE_RATE_REL)]
        private double PV_CompetitivenessJockeyPlaceRate(starter_info st, List<starter_info> sts)
        {
            double avgPlaceRate = sts.Sum(x => PV_JockeyPlacePercentage(x)) / sts.Count;
            return (PV_JockeyPlacePercentage(st) - avgPlaceRate);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_JOC_AVG_F_POS_REL)]
        private double PV_CompetitivenessJockeyAverageFPos(starter_info st, List<starter_info> sts)
        {
            double avgFinishingPosition = sts.Sum(x => PV_JockeyAverageFinishingPosition(x)) / sts.Count;
            return (PV_JockeyAverageFinishingPosition(st) - avgFinishingPosition);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_JOC_AVG_DISTBEATEN_REL)]
        private double PV_CompetitivenessJockeyAverageDisB(starter_info st, List<starter_info> sts)
        {
            double avgDistanceBeaten = sts.Sum(x => PV_JockeyAverageDistanceBeaten(x)) / sts.Count;
            return (PV_JockeyAverageDistanceBeaten(st) - avgDistanceBeaten);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_TRA_WIN_RATE_REL)]
        private double PV_CompetitivenessTrainerWinRate(starter_info st, List<starter_info> sts)
        {
            double avgWinRate = sts.Sum(x => PV_TrainerWinPercentage(x)) / sts.Count;
            return (PV_TrainerWinPercentage(st) - avgWinRate);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_TRA_PLACE_RATE_REL)]
        private double PV_CompetitivenessTrainerPlaceRate(starter_info st, List<starter_info> sts)
        {
            double avgPlaceRate = sts.Sum(x => PV_TrainerPlacePercentage(x)) / sts.Count;
            return (PV_TrainerPlacePercentage(st) - avgPlaceRate);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_TRA_AVG_F_POS_REL)]
        private double PV_CompetitivenessTrainerAverageFPos(starter_info st, List<starter_info> sts)
        {
            double avgFinishingPosition = sts.Sum(x => PV_TrainerAverageFinishingPosition(x)) / sts.Count;
            return (PV_TrainerAverageFinishingPosition(st) - avgFinishingPosition);
        }

        [PredictorVariablesAtt(PredictorVariableDefinitions.PVCAT_RACE_COMPETITIVENESS, PredictorVariableDefinitions.PV_COMP_TRA_AVG_DISTBEATEN_REL)]
        private double PV_CompetitivenessTrainerAverageDisB(starter_info st, List<starter_info> sts)
        {
            double avgDistanceBeaten = sts.Sum(x => PV_TrainerAverageDistanceBeaten(x)) / sts.Count;
            return (PV_TrainerAverageDistanceBeaten(st) - avgDistanceBeaten);
        }
        #endregion

        #endregion

        #region IDisposable members
        private bool disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    uow.Dispose();
                }
            }
            this.disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

    }
}