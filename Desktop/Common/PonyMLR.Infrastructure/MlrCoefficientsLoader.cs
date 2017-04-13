using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace PonyMLR.Infrastructure
{
    public class MlrCoefficientsLoader
    {
        private XDocument doc;
        private XNamespace ns;
        private XNamespace o_ns;
        private XNamespace x_ns;
        private XNamespace ss_ns;
        private XNamespace html_ns;
        private XElement wb;
        private XElement ws;

        public MlrCoefficientsLoader(string xmlfile)
        {
            this.doc = XDocument.Load(xmlfile);

            var allns = doc.Root.Attributes().
                    Where(a => a.IsNamespaceDeclaration).
                    GroupBy(a => a.Name.Namespace == XNamespace.None ? String.Empty : a.Name.LocalName,
                            a => XNamespace.Get(a.Value)).
                    ToDictionary(g => g.Key,
                                 g => g.First());

            foreach (var n in allns)
            {
                switch (n.Key)
                {
                    case "": ns = n.Value; break;
                    case "o": o_ns = n.Value; break;
                    case "x": x_ns = n.Value; break;
                    case "ss": ss_ns = n.Value; break;
                    case "html": html_ns = n.Value; break;
                    default: break;
                }
            }

            this.wb = doc.Elements().Where(r => r.Name.LocalName == "Workbook").First();
            this.ws = wb.Elements().Where(r => r.Name.LocalName == "Worksheet").First();
        }

        public bool VerifyFile(string track)
        {
            if (this.ws == null)
                return false;

            return (this.ws.Attribute(ss_ns + "Name").ToString().ToLower().Contains(track.ToLower()) ? true : false);
        }

        public Dictionary<string, double> GetAllCoefficients()
        {
            Dictionary<string, double> ret = new Dictionary<string, double>();
            XElement table = this.ws.Element(ns + "Table");
            if (table == null)
                return ret;

            IEnumerable<XElement> els = table.Elements(ns + "Row");
            foreach (XElement el in els)
            {
                if (el.Elements(ns + "Cell").First().Element(ns + "Data").Value.StartsWith("PV_") == true)
                {
                    string key = el.Elements(ns + "Cell").First().Element(ns + "Data").Value;
                    double pv = 0;
                    double.TryParse(el.Elements(ns + "Cell").Elements(ns + "Data").Where(x => (string)x.Attribute(ss_ns + "Type") == "Number").First().Value, out pv);
                    ret.Add(key, pv);
                }
            }

            return ret;
        }

        //TODO
        public double GetCoefficientByName(string name)
        {
            return 0;
        }

        //TODO
        // Get other parameters such as McFadden R-squared, Adjusted R-squared, Log-likelihood, Akaike criterion, Schwarz criterion, Hannan-Quinn etc.
        public double GetDvStandardDeviation()
        {
            double ret = 0;
            XElement table = this.ws.Element(ns + "Table");
            if (table == null)
                return ret;

            string sdname = "S.D. dependent var";
            try
            {
                XElement el = table.Elements(ns + "Row").Where(x => x.Value.Contains(sdname) == true).FirstOrDefault();
                string sdstr = el.Value.Substring(el.Value.IndexOf(sdname) + sdname.Length, el.Value.Length - (el.Value.IndexOf(sdname) + sdname.Length));
                ret = Convert.ToDouble(sdstr);
            }
            catch
            {
                return ret;
            }

            return ret;
        }
    }
}
