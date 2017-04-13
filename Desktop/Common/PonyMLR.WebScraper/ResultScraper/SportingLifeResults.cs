using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Net;
using PonyMLR.Infrastructure;
using PonyMLR.DataAccess;
using HtmlAgilityPack;

namespace PonyMLR.WebScraper
{
    public class SportingLifeResults : IRaceCardScraper<HtmlDocument,HtmlNode>, IRaceResultScraper
    {
        private String rootUrl = "http://www.sportinglife.com";  
        private String resultBaseUrl = "http://www.sportinglife.com/racing/results/";

        private DateTime dt;
        private List<Uri> _resultUrls = new List<Uri>();

        private Dictionary<int, String> trackDict;
        private Dictionary<int, Tuple<String, String>> trainerDict;
        private Dictionary<int, Tuple<String, String>> jockeyDict;

        public SportingLifeResults(String dt, Dictionary<int, String> trackDict, Dictionary<int, Tuple<String, String>> trainerDict, Dictionary<int, Tuple<String, String>> jockeyDict)
        {
            this.dt = DateTime.Parse(dt);
            this.trackDict = trackDict;
            this.trainerDict = trainerDict;
            this.jockeyDict = jockeyDict;
            GetAllResultPagesUrl(new Uri(resultBaseUrl + dt));
        }

        private void GetAllResultPagesUrl(Uri url)
        {            
            try
            {
                HttpWebRequest oReq = (HttpWebRequest)WebRequest.Create(url);
                HttpWebResponse resp = (HttpWebResponse)oReq.GetResponse();

                var doc = new HtmlDocument();

                doc.Load(resp.GetResponseStream());
                
                HtmlNodeCollection meetingNodes = doc.DocumentNode.SelectNodes("//section[@class='rac-dgp']");
                if (meetingNodes != null)
                {
                    foreach (HtmlNode meetingNode in meetingNodes)
                    {
                        HtmlNodeCollection raceNodes = meetingNode.SelectNodes(".//ul//li[@class='rac-cards click ']");
                        if (raceNodes != null)
                        {
                            foreach (HtmlNode raceNode in raceNodes)
                            {
                                String href = raceNode.SelectSingleNode(".//a[@class='ixa']").GetAttributeValue("href", "");

                                if (trackDict.Any(w => href.Substring(0, href.LastIndexOf('/')).Replace("-", " ").IndexOf(Utils.RemoveStringInBracket(w.Value), StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    _resultUrls.Add(new Uri(rootUrl + href));
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch(Exception e)
            {
                throw new Exception(e.Message);
            }
        }

        public List<Uri> ResultUrls
        {
            get { return this._resultUrls; }
            set { this._resultUrls = value; }
        }

        public HtmlNodeCollection GetAllHorseNodes(HtmlDocument doc)
        {
            return doc.DocumentNode.SelectNodes("//tbody//tr[not(@*)]");
        }

        public DateTime GetRaceDate(HtmlDocument doc)
        {         
            return this.dt;
        }

        public TimeSpan GetRaceTime(HtmlDocument doc)
        {
            String str = Regex.Match(doc.DocumentNode.SelectSingleNode("//section[@id='content']//nav//h1").InnerText, "([0-9]*)(:)([0-9]*)").ToString();
            TimeSpan time;
            TimeSpan.TryParse(str, out time);
            return time;
        }

        public T GetRaceTrack<T>(HtmlDocument doc)
        {
            int key = 0;
            String str = doc.DocumentNode.SelectSingleNode("//section[@id='content']//nav//h1").InnerText;
            foreach(String track in trackDict.Values)
            {
                if (str.IndexOf(Utils.RemoveStringInBracket(track), StringComparison.OrdinalIgnoreCase) >= 0)
                {                 
                    //check if it is Newmarket July or Rowley Mile
                    if (Utils.RemoveStringInBracket(track).ToLower().CompareTo("newmarket") == 0)
                    {
                        key = Utils.GetNewmarketCourseKey(dt);
                    }
                    else
                        key = trackDict.FirstOrDefault(x => x.Value == track).Key;

                    break;
                }
            }
          
            if(typeof(T) == typeof(int))
                return (T)(object)key;
            else
                return (T)(object)trackDict[key];
        }

        public String GetRaceName(HtmlDocument doc)
        {
            return doc.DocumentNode.SelectSingleNode("//div[@class='content-header']//h2").InnerText;
        }

        public String GetAgeRestriction(HtmlDocument doc)
        {
            string[] str = doc.DocumentNode.SelectSingleNode("//div[@class='content-header']//ul//li[1]").InnerText.TrimStart('(').Split(',');
            return str[0].Trim();
        }

        public int GetRaceClass(HtmlDocument doc)
        {
            string str = Regex.Match(doc.DocumentNode.SelectSingleNode("//div[@class='content-header']//ul//li[1]").InnerText, "(Class |Group )([1-7])").ToString();
            int x = 0;
            int.TryParse(Regex.Match(str, "([1-7])").ToString(), out x);

            return x;
        }

        public float GetDistance(HtmlDocument doc)
        {
            string str = doc.DocumentNode.SelectSingleNode("//div[@class='content-header']//ul//li[1]").InnerText;        
            string mileStr = Regex.Match(str, "([1-3])(m)").ToString();
            string furlongStr = Regex.Match(str, "([1-7])(f)").ToString();
            string yardStr = Regex.Match(str, "([0-9]*)(y,)").ToString();

            double furlong = 0;
            int yard = 0;
            int mile = 0;

            if (yardStr.CompareTo("") != 0)
                int.TryParse(yardStr.Substring(0, yardStr.Length - 2), out yard);
            else
                yard = 0;

            if (furlongStr.CompareTo("") != 0)
                double.TryParse(furlongStr.Substring(0, furlongStr.Length - 1), out furlong);
            else
                furlong = 0;

            if (mileStr.CompareTo("") != 0)
                int.TryParse(mileStr.Substring(0, mileStr.Length - 1), out mile);
            else
                mile = 0;

            furlong = Math.Round(Convert.ToDouble(yard+(furlong*200)+(mile*1600))/100, MidpointRounding.AwayFromZero)/2;

            return (float)furlong;
        }

        public int GetPrizeMoney(HtmlDocument doc)
        {
            int i = 0; 

            try
            {
                string[] str = doc.DocumentNode.SelectSingleNode("//div[@class='content-header']//ul//li[2]").InnerText.Split(' ');
                string[] str2 = str.FirstOrDefault(x => x.Contains("pound")).Split(';');
                string str3 = Regex.Replace(str2[1], "(,)", "").ToString().Trim();

                int.TryParse(str3, out i);
            }
            catch { }

            return i;
        }

        public String GetGoingDescription(HtmlDocument doc)
        {
            string str = doc.DocumentNode.SelectSingleNode("//div[@class='content-header']//ul//li[3]").InnerText;
            string[] parts = str.Split(':');
            string[] parts2 = parts[1].Split('(');
            string[] parts3 = parts2[0].Split(',');

            return (Regex.Replace(parts3[0], "(to)", "To")).Trim();
        }

        public int GetNumberOfRunners(HtmlDocument doc)
        {
            string str = doc.DocumentNode.SelectSingleNode("//div[@class='content-header']//ul//li[1]").InnerText;
            string runnerStr = Regex.Match(str, "([0-9]*)( runners)").ToString();
            int x = 1;
            int.TryParse(Regex.Replace(runnerStr, "runners", "").Trim(), out x);
            return x;
        }

        public String GetHorseName(HtmlNode node)
        {
            return node.SelectSingleNode(".//td[@class='ixt'][1]//strong[@class='name']//a[@href]").InnerText.Trim();
        }

        public int GetStall(HtmlNode node)
        {
            string[] str = node.SelectSingleNode(".//td[1]").InnerText.Trim().Split(' ');
            int x = 0;
            if (str.Length > 0)
                int.TryParse(str[str.Length - 1].TrimStart('(').TrimEnd(')'), out x);

            return x;
        }

        public int GetHorseAge(HtmlNode node)
        {
            int x = 0;
            int.TryParse(node.SelectSingleNode(".//td[5]").InnerText, out x);
            return x;
        }

        public T GetTrainerName<T>(HtmlNode node)
        {
            string str = node.SelectSingleNode(".//td[@class='ixt'][2]//a[@class='name']").InnerText.Trim();
            T trainer = Utils.ConvertName<T>(str, trainerDict);

            return (T)(object)trainer;
        }

        public T GetJockeyName<T>(HtmlNode node)
        {
            string str = node.SelectSingleNode(".//td[@class='ixt'][3]//a[@class='name']").InnerText.Trim();
            T jockey = Utils.ConvertName<T>(str, jockeyDict);

            return (T)(object)jockey;
        }

        public int GetJockeyClaim(HtmlNode node)
        {
            string str = node.SelectSingleNode(".//td[@class='ixt'][3]//a[@class='name']").InnerText.Trim();
            string claim = Regex.Match(str, "(\\([0-9]\\))").ToString().Trim().TrimStart('(').TrimEnd(')');
            int n;
            if (int.TryParse(claim, out n))
            {
                return n;
            }

            return 0;
        }

        public int GetWeightCarried(HtmlNode node)
        {
            return Utils.StoneToPound(Regex.Match(node.SelectSingleNode(".//td[6]").InnerText, "([0-9]*)(-)([0-9]*)").ToString());          
        }

        public double GetOdds(HtmlNode node)
        {
            string str = node.SelectSingleNode(".//td[@class='mobile-hdn']").InnerText.Trim();
            if ((str.ToLower().Contains("evens") == true) || (str.ToLower().Contains("even") == true) || (str.ToLower().Contains("evn") == true) || (str.ToLower().Contains("evs") == true))
                return 1;

            return Utils.FractionalOddsToDecimal(str);
        }

        public bool GetFavouritismStatus(HtmlNode node)
        {
            string str = Regex.Match(node.SelectSingleNode(".//td[@class='mobile-hdn']").InnerText.Trim(), "(f|j|c)").ToString();
            if (str.CompareTo("") != 0)
                return true;

            return false;
        }

        public double GetCompletionTime(HtmlDocument doc)
        {
            string str = doc.DocumentNode.SelectSingleNode("//div[@class='racecard-status']//ul//li[1]").InnerText.Split(':')[1].Trim();
            double time = Utils.MinSecToSeconds(str);
            if (time == 0)
                return Globals.UNDEFINED_FINISHING_TIME;
            else
                return time;

        }

        public int GetFinishingPosition(HtmlNode node, int runners)
        {
            string str = Regex.Replace(node.SelectSingleNode(".//td[1]").InnerText, "(\\n|st|nd|rd|th)", " ");
            int x;
            if (int.TryParse(str.Trim().Split(' ')[0], out x) == true)
            {
                return x;
            }

            return runners;
        }

        public double GetDistanceBeaten(HtmlNode node)
        {
            string str = node.SelectSingleNode(".//td[2]").InnerText;
            switch(str.ToLower())
            {     
                case "":
                    return 0;
                case "nse":
                    return (double)0.05;
                case "sh":
                case "s.h":
                    return (double)0.1;
                case "hd":
                    return (double)0.2;
                case "nk":
                    return (double)0.3;
                default:
                    float d = 0;
                    string fracStr = Regex.Match(str, "(&frac)([1-4]*)").ToString().Replace("&frac", "");
                    if (fracStr.CompareTo("") != 0 && fracStr.Length == 2)
                    {
                        char[] fs = fracStr.ToCharArray();
                        float x = 1;
                        float y = 1;
                        float.TryParse(fs[0].ToString(), out x);
                        float.TryParse(fs[1].ToString(), out y);
                        float frac = x / y;                       
                        float.TryParse(str.Trim().Split('&')[0], out d);

                        return (double)(d + frac);
                    }
                    else
                    {
                        float.TryParse(str.Trim(), out d);
                        return (double)d;
                    }
            }
        }

        public string GetRaceComment(HtmlNode node)
        {
            HtmlNode cnode;

            try
            {
                cnode = node.SelectSingleNode("following::tr[@class='note']").SelectSingleNode(".//td/text()[2]");
            }
            catch (Exception)
            {
                return "";
            }

            if (cnode == null)
                return "";

            return cnode.InnerText.Trim();
        }
    }
}
