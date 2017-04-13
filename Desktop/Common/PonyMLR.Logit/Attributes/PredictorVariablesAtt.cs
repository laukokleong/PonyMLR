using System;

namespace PonyMLR.Logit
{
    public class PredictorVariablesAtt : Attribute
    {
        private string category;
        private string name;

        public PredictorVariablesAtt(string category, string name)
        {
            this.category = category;
            this.name = name;
        }

        public string Category { get { return category; } }

        public string Name { get { return name; } }
    }
}
