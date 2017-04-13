using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.ComponentModel;
using System.Collections.ObjectModel;
using PonyMLR.DataAccess;
using PonyMLR.Infrastructure;

namespace PonyMLR.Logit
{
    public class RaceCardModel
    {
        public DateTime race_date { get; set; }
        public KeyValuePair<int, String> racetrack { get; set; }
        public List<race_card> races { get; set; }

        public Dictionary<string, double> mlrStepOneCoefficients { get; set; }
        public Dictionary<string, double> mlrStepTwoCoefficients { get; set; }
        public double dependentVarStdDeviation { get; set; }

        public RaceCardModel(DateTime dt, KeyValuePair<int, String> racetrack)
        {
            this.race_date = dt;
            this.racetrack = racetrack;
            this.mlrStepOneCoefficients = new Dictionary<string, double>();
            this.mlrStepTwoCoefficients = new Dictionary<string, double>();
        }
    }

    public class race_card
    {
        public race_card()
        {
            this.starters = new ObservableCollection<starter_info>();
        }

        public int id { get; set; }
        public TimeSpan race_time { get; set; }
        public string race_name { get; set; }
        public string race_restrictions_age { get; set; }
        public int race_class { get; set; }
        public decimal race_distance { get; set; }
        public int race_prize_money { get; set; }
        public string race_going { get; set; }
        public int race_number_of_runners { get; set; }
        public bool has_first_time_out { get; set; }

        public double finishing_time { get; set; }

        public ObservableCollection<starter_info> starters { get; set; }
    }

    public partial class starter_info
    {
        public starter_info()
        {
            this.previousRaces = new List<race_result>();
            this.predictorVariables = new Dictionary<string, double>();
            this.mlr1 = new mlrOne_Result();
            this.mlr2 = new mlrTwo_Result();
        }

        public string horse_name { get; set; }
        public int horse_age { get; set; }
        public int stall { get; set; }
        public KeyValuePair<int, String> trainer_name { get; set; }
        public KeyValuePair<int, String> jockey_name { get; set; }
        public int jockeys_claim { get; set; }
        public int pounds { get; set; }
        public double odds { get; set; }
        public double early_speed { get; set; }
        public double lstakeProfit { get; set; }
        public double hpOverRace { get; set; }
        public bool is_favourite { get; set; }

        public List<race_result> previousRaces { get; set; }
        public Dictionary<string, double> predictorVariables { get; set; }

        public mlrOne_Result mlr1 { get; set; }
        public mlrTwo_Result mlr2 { get; set; }

        public bool underestimated { get; set; }
        public BetVerdict verdict { get; set; }
        public double kellyPercentage { get; set; }
        public OddsMovement oddsMovement { get; set; }

        public int finishing_position { get; set; }
        public double distance_beaten { get; set; }
    }

    public class mlrOne_Result
    {
        public mlrOne_Result()
        {
            this.predictorVariables = new Dictionary<string, double>();
            this.predictorVariables.Add(PredictorVariableDefinitions.PV_MLRS1_PUBLIC_ODDS, 0);
            this.predictorVariables.Add(PredictorVariableDefinitions.PV_MLRS1_HORSE_SCORE, 0);
            this.predictorVariables.Add(PredictorVariableDefinitions.PV_MLRS1_PUBLIC_PROB, 0);
            this.predictorVariables.Add(PredictorVariableDefinitions.PV_MLRS1_FUNDAM_PROB, 0);
            this.predictorVariables.Add(PredictorVariableDefinitions.PV_MLRS1_LOG_PUBLIC_PROB, 0);
            this.predictorVariables.Add(PredictorVariableDefinitions.PV_MLRS1_LOG_FUNDAM_PROB, 0);
            this.predictorVariables.Add(PredictorVariableDefinitions.PV_MLRS1_LOG_PUBLIC_ODDS, 0);
            this.predictorVariables.Add(PredictorVariableDefinitions.PV_MLRS1_LOG_HORSE_SCORE, 0);
        }

        public double odds_public { get; set; }
        public double odds_fundamental { get; set; }
        public double probability_public { get; set; }
        public double probability_fundamental { get; set; }

        public Dictionary<string, double> predictorVariables { get; set; }
    }

    public class mlrTwo_Result
    {
        public double odds_fair { get; set; }
        public double odds_actual { get; set; }
        public double odds_deviation { get; set; }
        public double probability_fair { get; set; }
        public double probability_actual { get; set; }
    }


}