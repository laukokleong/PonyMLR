using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using PonyMLR.Infrastructure;
using HtmlAgilityPack;

namespace PonyMLR.WebScraper
{
    public class SportingLifeRaceCards : IRaceCardScraper<HtmlNode, HtmlNode>
    {
        private String rootUrl = "http://www.sportinglife.com";
        private String raceCardBaseUrl = "http://www.sportinglife.com/racing/racecards/";

        private DateTime dt;
        private List<List<Uri>> _racecardUrls = new List<List<Uri>>();

        private Dictionary<int, String> trackDict;
        private Dictionary<int, Tuple<String, String>> trainerDict;
        private Dictionary<int, Tuple<String, String>> jockeyDict;

        public SportingLifeRaceCards(String dt, Dictionary<int, String> trackDict, Dictionary<int, Tuple<String, String>> trainerDict, Dictionary<int, Tuple<String, String>> jockeyDict)
        {
            DateTime.TryParse(dt, out this.dt);
            this.trackDict = trackDict;
            this.trainerDict = trainerDict;
            this.jockeyDict = jockeyDict;

            if (dt.CompareTo("") != 0)
                GetAllRaceCardPagesUrl(new Uri(raceCardBaseUrl + dt));
        }

        private void GetAllRaceCardPagesUrl(Uri url)
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
                        HtmlNodeCollection raceNodes = meetingNode.SelectNodes(".//ul//li[@class='rac-cards  click']");
                        if (raceNodes != null)
                        {                          
                            List<Uri> singleMeetingList = new List<Uri>();
                            foreach (HtmlNode raceNode in raceNodes)
                            {
                                string href = raceNode.SelectSingleNode(".//a[@class='ixa']").GetAttributeValue("href", "");
                                string track = href.Split('/')[4];

                                if (trackDict.Any(w => track.IndexOf(Utils.RemoveStringInBracket(w.Value), StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    singleMeetingList.Add(new Uri(rootUrl + href));
                                }
                                else
                                {
                                    break;
                                }
                            }

                            if (singleMeetingList.Count != 0)
                            {
                                _racecardUrls.Add(singleMeetingList);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }
        }

        public HtmlNode CreateRaceCard(HtmlDocument doc)
        {
            HtmlDocument rc = new HtmlDocument();

            HtmlNode header = doc.DocumentNode.SelectSingleNode("//section[@class='racecard-header']");
            HtmlNode details = doc.DocumentNode.SelectSingleNode("//div[@class='tab-x']");

            rc.DocumentNode.AppendChild(header);
            rc.DocumentNode.AppendChild(details);

            return rc.DocumentNode;
        }

        public List<List<Uri>> RaceCardUrls
        {
            get { return this._racecardUrls; }
            set { this._racecardUrls = value; }
        }

        #region Race Info
        public HtmlNodeCollection GetAllRaceNodes(HtmlDocument doc)
        {
            return doc.DocumentNode.SelectNodes("//racecard//race");
        }

        public DateTime GetRaceDate(HtmlNode node)
        {
            return this.dt;
        }

        public TimeSpan GetRaceTime(HtmlNode node)
        {
            String str = Regex.Match(node.SelectSingleNode(".//table").GetAttributeValue("summary", "").ToString(), "([0-9]*)(:)([0-9]*)").ToString();
            TimeSpan time;
            TimeSpan.TryParse(str, out time);
            return time;
        }

        public T GetRaceTrack<T>(HtmlNode node)
        {
            return default(T);
        }

        public String GetRaceName(HtmlNode node)
        {
            return node.SelectSingleNode(".//section[@class='racecard-header']//div//div[@class='content-header']//h2").InnerText;
        }

        public String GetAgeRestriction(HtmlNode node)
        {
            string[] str = node.SelectSingleNode(".//section[@class='racecard-header']//div//div[@class='content-header']//ul[@class='list']//li[1]").InnerText.TrimStart('(').Split(',');
            return str[0].Trim();
        }

        public int GetRaceClass(HtmlNode node)
        {
            string str = Regex.Match(node.SelectSingleNode(".//section[@class='racecard-header']//div//div[@class='content-header']//ul[@class='list']//li[1]").InnerText, "(Class |Group )([1-7])").ToString();
            int x = 0;
            int.TryParse(Regex.Match(str, "([1-7])").ToString(), out x);

            return x;
        }

        public float GetDistance(HtmlNode node)
        {
            string str = node.SelectSingleNode(".//section[@class='racecard-header']//div//div[@class='content-header']//ul[@class='list']//li[1]").InnerText;
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

            furlong = Math.Round(Convert.ToDouble(yard + (furlong * 200) + (mile * 1600)) / 100, MidpointRounding.AwayFromZero) / 2;

            return (float)furlong;
        }

        public int GetPrizeMoney(HtmlNode node)
        {
            string[] str = node.SelectSingleNode(".//section[@class='racecard-header']//div//div[@class='content-header']//ul[@class='list']//li[2]").InnerText.Split(' ');
            string win_pmon = str.FirstOrDefault(x => x.Contains("pound"));
            if (win_pmon == null)
                return 0;

            string[] str2 = win_pmon.Split(';');
            string str3 = Regex.Replace(str2[1], "(,)", "").ToString().Trim();

            int i = 0;
            int.TryParse(str3, out i);

            return i;
        }

        public String GetGoingDescription(HtmlNode node)
        {
            try
            {
                string str = node.SelectSingleNode(".//section[@class='racecard-header']//div//div[@class='content-header']//ul[@class='list']//li[3]").InnerText;
                string[] parts = str.Split(':');
                if (parts[0].Trim().CompareTo("Going") != 0)
                    return "";

                string[] parts2 = parts[1].Split('(');
                string[] parts3 = parts2[0].Split(',');

                return Utils.GoingDescriptionConstructor(Regex.Replace(parts3[0], "(to)", "To")).Trim();
            }
            catch
            {
                return "";
            }                     
        }

        public int GetNumberOfRunners(HtmlNode node)
        {
            string str = node.SelectSingleNode(".//section[@class='racecard-header']//div//div[@class='content-header']//ul[@class='list']//li[1]").InnerText;
            string runnerStr = Regex.Match(str, "([0-9]*)( runners)").ToString();
            int x = 1;
            int.TryParse(Regex.Replace(runnerStr, "runners", "").Trim(), out x);
            return x;
        }
        #endregion

        #region Starters Info
        public HtmlNodeCollection GetAllStarterNodes(HtmlNode node)
        {
            return node.SelectNodes(".//table[1]//tbody/tr");
        }

        public String GetHorseName(HtmlNode node)
        {
            return node.SelectSingleNode(".//td[@class='ixt']//div[@class='horse-dtl']//strong[@class='name']//a").InnerText.Trim();
        }

        public int GetStall(HtmlNode node)
        {
            string str = node.SelectSingleNode(".//td//em[@class='stall']").InnerText.Trim();
            int x = 0;
            int.TryParse(str.TrimStart('(').TrimEnd(')'), out x);

            return x;
        }

        public int GetHorseAge(HtmlNode node)
        {
            int x = 0;
            int.TryParse(Regex.Match(node.SelectSingleNode(".//td[@class='ixt']/ul[@class='mobile-dtl']//li[2]").InnerText, "([0-9]+)").ToString(), out x);
            return x;
        }

        public T GetTrainerName<T>(HtmlNode node)
        {
            string[] strs = node.SelectSingleNode(".//td[@class='ixt']//div[@class='horse-dtl']//ul[@class='mobile-dtl']//li[1]").InnerText.Split(':');
            if (strs.Length < 1)
                return default(T);

            T trainer = Utils.ConvertName<T>(strs[1].Trim(), trainerDict);

            return (T)(object)trainer;
        }

        public T GetJockeyName<T>(HtmlNode node)
        {
            string[] strs = node.SelectSingleNode(".//td[@class='ixt']//div[@class='horse-dtl']//ul[@class='mobile-dtl']//li[2]").InnerText.Split(':');
            if (strs.Length < 1)
                return default(T);

            T jockey = Utils.ConvertName<T>(strs[1].Trim(), jockeyDict);

            return (T)(object)jockey;
        }

        public int GetJockeyClaim(HtmlNode node)
        {
            string str = node.SelectSingleNode(".//td[@class='ixt mobile-hdn'][2]").InnerText.Trim();
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
            return Utils.StoneToPound(Regex.Match(node.SelectSingleNode(".//td[@class='ixt']//ul[@class='mobile-dtl']//li[3]").InnerText, "([0-9]*)(-)([0-9]*)").ToString());
        }

        public double GetOdds(HtmlNode node)
        {
            HtmlNode oNode = node.SelectSingleNode(".//td[last()]/a");
            if (oNode == null)
                return Globals.UNDEFINED_ODDS;

            string str = oNode.InnerText.Trim();
            if ((str.ToLower().Contains("evens") == true) || (str.ToLower().Contains("even") == true) || (str.ToLower().Contains("evn") == true) || (str.ToLower().Contains("evs") == true))
                return 1;

            return Utils.FractionalOddsToDecimal(str);
        }

        public bool GetFavouritismStatus(HtmlNode node)
        {
            HtmlNode oNode = node.SelectSingleNode(".//td[last()]/a");
            if (oNode == null)
                return false;

            string str = Regex.Match(oNode.InnerText.Trim(), "(f|j|c)").ToString();
            if (str.CompareTo("") != 0)
                return true;

            return false;
        }
        #endregion

    }
}
