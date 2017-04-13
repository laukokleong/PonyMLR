using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using PonyMLR.Infrastructure;
using PonyMLR.WebScraper;
using HtmlAgilityPack;

namespace PonyMLR.Modules.Calculate
{
    public class OddsUpdater
    {
        //private ToteSportOdds scraper;
        private TotePoolLiveInfoOdds scraper;
        private string track;
        private Uri meetingUrl;

        public OddsUpdater(string track, DateTime date)
        {       
            //mobile totesport odds page outdated
            //scraper = new ToteSportOdds();
            //this.track = track;
            //this.meetingUrl = scraper.GetOddsUrl(null, track);

            scraper = new TotePoolLiveInfoOdds();
            this.track = Utils.RemoveStringInBracket(track).Replace(' ', '-');
            if (this.track.ToLower() == "lingfield" || this.track.ToLower() == "kempton" || this.track.ToLower() == "haydock" || this.track.ToLower() == "sandown")
                this.track = this.track + "-park";
            else if (this.track.ToLower() == "catterick")
                this.track = this.track + "-bridge";

            this.meetingUrl = new Uri(scraper.rootUrl + @"/" + date.Year.ToString("D4") + @"/" + date.Month.ToString("D2") + @"/" + date.Day.ToString("D2") + @"/" + this.track.ToLower());
        }

        public T GetOddsDoc<T>(string time)
        {
            try
            {
                //Uri url = scraper.GetOddsUrl(meetingUrl, time);
                Uri url = new Uri(meetingUrl.ToString() + @"/" + "win-place" + @"/" + time.ToString());

                HttpWebRequest oReq = (HttpWebRequest)WebRequest.Create(url.ToString());
                oReq.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;

                HttpWebResponse resp = (HttpWebResponse)oReq.GetResponse();

                var doc = new HtmlDocument();
                doc.Load(resp.GetResponseStream());

                object ret = null;
                if (typeof(T) == typeof(string))
                    ret = (string)doc.DocumentNode.InnerHtml;
                else if (typeof(T) == typeof(HtmlDocument))
                    ret = doc;

                return (T)ret;
            }
            catch
            {
                return default(T);
            }
        }

        public double UpdateOdds<T>(T html, string starter)
        {
            if (html == null)
                return Globals.UNDEFINED_ODDS;

            HtmlDocument doc;
            if (typeof(T) == typeof(HtmlDocument))
                doc = (HtmlDocument)(object)html;
            if (typeof(T) == typeof(string))
            {
                doc = new HtmlDocument();
                doc.LoadHtml((string)(object)html);
            }
            else
                return Globals.UNDEFINED_ODDS;

            return scraper.GetOdds(doc, starter);
        }
    }
}
