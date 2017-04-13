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
    public class TotePoolLiveInfoOdds
    {
        public String rootUrl = "http://www.totepoolliveinfo.com";

        public TotePoolLiveInfoOdds()
        {

        }

        public Uri GetOddsUrl(Uri url, string param)
        {
            return null;
        }

        public double GetOdds(HtmlDocument doc, string entry)
        {
            try
            {
                HtmlNodeCollection divNodes = doc.DocumentNode.SelectNodes(@"//body/div/div[@class='content']/div[@class='win-place']
                                                                            /div/div[@class='win-place-pre-race']/div[@class='win-place-pre-race-inner-container']
                                                                            /div[@class='win-place-pre-race-details']/div[@class='win-place-scrolled']
                                                                            /table[@class='win-place-pre-race-details-horses']/tbody[@class='win-place-pre-race-details-horses-table-body']
                                                                            /tr[@class='win-place-pre-race-details-horses-table-row']");

                if (divNodes != null)
                {
                    string estr = Utils.RemoveStringInBracket(entry).ToLower().Replace("'", String.Empty);
                    estr = estr.Substring(0, estr.Length < 10 ? estr.Length : 10);
                    HtmlNode div = divNodes.Where(x => x.InnerText.ToString().ToLower().Replace("'", String.Empty).Replace(@"&#39;", String.Empty).Trim().Contains(estr) == true).LastOrDefault();

                    if (div == null)
                        return Globals.UNDEFINED_ODDS;

                    string odds_str = div.SelectSingleNode(@".//td[@class='win-place-pre-race-details-horses-table-body-cell win-place-pre-race-details-horses-table-body-horse-approx-price']").InnerText;
                    if (odds_str == "")
                        return Globals.UNDEFINED_ODDS;

                    if ((odds_str.ToLower().Contains("evens") == true) || (odds_str.ToLower().Contains("even") == true) || (odds_str.ToLower().Contains("evn") == true) || (odds_str.ToLower().Contains("evs") == true))
                        return 1;

                    return Utils.FractionalOddsToDecimal(odds_str);
                }
            }
            catch (Exception) { }

            return Globals.UNDEFINED_ODDS;
        }
    }
}
