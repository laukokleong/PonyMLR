using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.SolverFoundation.Common;
using Microsoft.SolverFoundation.Services;

namespace PonyMLR.Infrastructure
{
    public class KellyParam
    {
        // A unique identifier for the selection
        public int Id { get; set; }

        // Name of selection
        public string Name { get; set; }

        // Fair probability
        public double FairProbability { get; set; }

        // Actual Odds
        public double ActualOdds { get; set; }

        // Kelly Fraction
        public double Fraction { get; set; }
    }

    public class KellyCalculator
    {
        private double fraction;
        public KellyParam[] kParams;

        public KellyCalculator()
        {
            
        }

        public KellyCalculator(double fraction)
        {
            this.fraction = fraction;
        }

        public KellyCalculator(KellyParam[] param)
        {
            this.kParams = param;
        }

        public double GetKellyFraction(double odds, double prob)
        {
            if ((prob > 1) || (prob < 0))
                return 0;

            return ((((odds - 1) * prob) - (1 - prob)) / (odds - 1));
        }

        public void SolveKellyCriterion()
        {
            SolverContext context = SolverContext.GetContext();
            Model model = context.CreateModel();


            Set selections = new Set(Domain.Integer, "selections");

            // set parameters
            Parameter prob = new Parameter(Domain.RealNonnegative, "prob", selections);
            prob.SetBinding(this.kParams, "FairProbability", "Id");

            Parameter odds = new Parameter(Domain.RealNonnegative, "odds", selections);
            odds.SetBinding(this.kParams, "ActualOdds", "Id");

            model.AddParameters(prob, odds);

            // set decision
            Decision fraction = new Decision(Domain.RealRange(0, 1), "fraction", selections);
            fraction.SetBinding(this.kParams, "Fraction", "Id");

            model.AddDecisions(fraction);

            // add constraint
            model.AddConstraint("bank", Model.Sum(Model.ForEach(selections, s => fraction[s])) <= 1);

            // set goal
            Goal gf = model.AddGoal("gf", GoalKind.Maximize, Model.Sum(Model.ForEach(selections,
                s => prob[s] * Model.Log(1 + (fraction[s] * odds[s]) - Model.Sum(Model.ForEach(selections, t=>fraction[t]))))));

            // solve it and write decision
            context.Solve();
            context.PropagateDecisions();

            // clear model
            context.ClearModel();
        }
    }
}
