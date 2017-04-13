using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PonyMLR.Infrastructure;

namespace PonyMLR.Logit
{
    public class LogitCalc
    {
        Dictionary<string, double> mlr1coeff;
        Dictionary<string, double> mlr2coeff;

        public LogitCalc(Dictionary<string, double> mlr1coeff, Dictionary<string, double> mlr2coeff)
        {
            this.mlr1coeff = mlr1coeff;
            this.mlr2coeff = mlr2coeff;
        }

        public void CalculateAllEntriesPrediction(race_card race)
        {
            double sumStartersInverseOdds = race.starters.Sum(x => 1 / (x.odds + 1));

            // MLR Step 1
            double sumStartersScoreStep1 = race.starters.Select(x => CalculateSingleEntryScore(mlr1coeff, x.predictorVariables)).Sum();
            foreach (starter_info st in race.starters)
            {               
                st.mlr1.odds_public = st.odds + 1;
                st.mlr1.probability_public = (1 / st.mlr1.odds_public) / sumStartersInverseOdds;

                // stop here if there is first time out
                if (race.has_first_time_out == true)
                    continue;

                double score_step1 = CalculateSingleEntryScore(mlr1coeff, st.predictorVariables);
                st.mlr1.probability_fundamental = score_step1 / sumStartersScoreStep1;
                st.mlr1.odds_fundamental = 1 / st.mlr1.probability_fundamental;

                // populate data for Step 2
                st.mlr1.predictorVariables[PredictorVariableDefinitions.PV_MLRS1_PUBLIC_ODDS] = st.mlr1.odds_public;
                st.mlr1.predictorVariables[PredictorVariableDefinitions.PV_MLRS1_HORSE_SCORE] = score_step1;
                st.mlr1.predictorVariables[PredictorVariableDefinitions.PV_MLRS1_PUBLIC_PROB] = st.mlr1.probability_public;
                st.mlr1.predictorVariables[PredictorVariableDefinitions.PV_MLRS1_FUNDAM_PROB] = st.mlr1.probability_fundamental;
                st.mlr1.predictorVariables[PredictorVariableDefinitions.PV_MLRS1_LOG_PUBLIC_PROB] = Math.Log(st.mlr1.probability_public);
                st.mlr1.predictorVariables[PredictorVariableDefinitions.PV_MLRS1_LOG_FUNDAM_PROB] = Math.Log(st.mlr1.probability_fundamental);
                st.mlr1.predictorVariables[PredictorVariableDefinitions.PV_MLRS1_LOG_PUBLIC_ODDS] = Math.Log(st.mlr1.odds_public);
                st.mlr1.predictorVariables[PredictorVariableDefinitions.PV_MLRS1_LOG_HORSE_SCORE] = Math.Log(score_step1);                
            }

            // MLR Step 2
            double sumStartersScoreStep2 = (mlr2coeff.Count() != 0 ? race.starters.Select(x => CalculateSingleEntryScore(mlr2coeff, x.mlr1.predictorVariables)).Sum() : sumStartersScoreStep1);
            foreach (starter_info st in race.starters)
            {
                st.mlr2.odds_actual = st.odds + 1;
                st.mlr2.probability_actual = (1 / st.mlr2.odds_actual) / sumStartersInverseOdds;

                // stop here if there is first time out
                if (race.has_first_time_out == true)
                    continue;

                double score_step2 = (mlr2coeff.Count() != 0 ? CalculateSingleEntryScore(mlr2coeff, st.mlr1.predictorVariables) : st.mlr1.predictorVariables[PredictorVariableDefinitions.PV_MLRS1_HORSE_SCORE]);
                st.mlr2.probability_fair = score_step2 / sumStartersScoreStep2;
                if (st.mlr2.probability_fair < 0)
                    st.mlr2.probability_fair = 1e-12;

                st.mlr2.odds_fair = 1 / st.mlr2.probability_fair;
                if (st.mlr2.odds_fair > Globals.UNDEFINED_ODDS)
                    st.mlr2.odds_fair = Globals.UNDEFINED_ODDS;

                st.mlr2.odds_deviation = (st.mlr2.odds_actual - st.mlr2.odds_fair) / st.mlr2.odds_fair;
                st.underestimated = (st.mlr2.odds_actual > st.mlr2.odds_fair ? true : false);
            }
        }

        private double CalculateSingleEntryScore(Dictionary<string, double> mlrcoeff, Dictionary<string, double> predictorVariables)
        {
            var query = predictorVariables.Join(mlrcoeff, p => p.Key, m => m.Key, (p, m) => new { pv = p.Value, mc = m.Value });
            return Math.Exp(query.ToDictionary(x => x.mc, x => x.pv).Select(p=>p.Key * p.Value).Sum());
        }
    }
}
