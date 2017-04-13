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
    public class ToteSportOdds
    {
        public String rootUrl = "https://mobile.totesport.com";

        public ToteSportOdds()
        {

        }

        public Uri GetOddsUrl(Uri url, string param)
        {
            if (url == null)
                url = new Uri(rootUrl);

            try
            {
                HttpWebRequest oReq = (HttpWebRequest)WebRequest.Create(url.ToString());
                HttpWebResponse resp = (HttpWebResponse)oReq.GetResponse();

                var doc = new HtmlDocument();

                doc.Load(resp.GetResponseStream());

                HtmlNodeCollection divNodes = doc.DocumentNode.SelectNodes("//body/div[@class='menutableBackOdd']/table/tr/td/a | //body/div[@class='menutableBackEven']/table/tr/td/a");
                if (divNodes != null)
                {
                    // could be looking for meeting page or single event
                    string nodeType;
                    if (Regex.Match(param, "([0-9]*)(:)([0-9]*)").ToString().CompareTo("") != 0)
                        nodeType = "EVENT";
                    else
                        nodeType = "TODAY";

                    // get from last because the first few entries are specific race with race time
                    HtmlNode div = divNodes.Where(x => x.InnerText.ToString().Trim().Contains(Utils.RemoveStringInBracket(param)) == true)
                                               .Where(y => y.GetAttributeValue("href", "").Contains("dataBrowsing") == true)
                                               .Where(y => y.GetAttributeValue("href", "").Contains(nodeType) == true)
                                               .LastOrDefault();

                    if (div == null)
                        return null;

                    string href = div.GetAttributeValue("href", "");
                    Uri ret = new Uri(rootUrl + href);

                    return ret;
                }
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }

            return null;
        }

        public double GetOdds(HtmlDocument doc, string entry)
        {
            try
            {
                HtmlNodeCollection divNodes = doc.DocumentNode.SelectNodes("//body/div[@class='menutableBackOdd']/table/tr/td/a | //body/div[@class='menutableBackEven']/table/tr/td/a");
                if (divNodes != null)
                {
                    HtmlNode div = divNodes.Where(x => x.InnerText.ToString().ToLower().Trim().Contains(Utils.RemoveStringInBracket(entry).ToLower()) == true).LastOrDefault();
                    if (div == null)
                        return Globals.UNDEFINED_ODDS;

                    string text = div.InnerText;
                    if ((text.ToLower().Contains("evens") == true) || (text.ToLower().Contains("even") == true) || (text.ToLower().Contains("evn") == true) || (text.ToLower().Contains("evs") == true))
                        return 1;

                    string odds_str = Regex.Match(text, "([0-9]*)(/)([0-9]*)").ToString();

                    return Utils.FractionalOddsToDecimal(odds_str);
                }
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }

            return Globals.UNDEFINED_ODDS;
        }
    }
}
