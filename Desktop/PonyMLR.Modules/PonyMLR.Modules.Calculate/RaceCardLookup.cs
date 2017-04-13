using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using PonyMLR.Infrastructure;
using PonyMLR.DataAccess;
using PonyMLR.WebScraper;
using PonyMLR.Logit;
using HtmlAgilityPack;

namespace PonyMLR.Modules.Calculate
{
    public class RaceCardLookup
    {
        private SportingLifeRaceCards scraper;

        public RaceCardLookup(Nullable<DateTime> dt, Dictionary<int, String> trackDict = null, Dictionary<int, Tuple<String, String>> trainerDict = null, Dictionary<int, Tuple<String, String>> jockeyDict = null)
        {
            String dtStr = "";

            if (dt != null)
                dtStr = dt.Value.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

            scraper = new SportingLifeRaceCards(dtStr, trackDict, trainerDict, jockeyDict);
        }

        public List<List<Uri>> GetRaceCardsUrl()
        {
            return scraper.RaceCardUrls;
        }

        public String SaveSingleMeetingAsHtml(List<Uri> rl)
        {
            HtmlDocument doc2save = new HtmlDocument();
            HtmlNode racecard = HtmlNode.CreateNode("<racecard></racecard>");
            String fileName = null;

            foreach (Uri link in rl)
            {
                try
                {
                    HttpWebRequest oReq = (HttpWebRequest)WebRequest.Create(link);
                    HttpWebResponse resp = (HttpWebResponse)oReq.GetResponse();

                    var doc = new HtmlAgilityPack.HtmlDocument();

                    doc.Load(resp.GetResponseStream());

                    HtmlNode singleRaceNodes = scraper.CreateRaceCard(doc);

                    //create a new race node
                    HtmlNode race = HtmlNode.CreateNode("<race></race>");
                    race.AppendChild(singleRaceNodes);

                    //add to main racecard
                    racecard.AppendChild(race);
                }
                catch (Exception e)
                {
                    throw new Exception(e.Message);
                }

                if (fileName == null)
                    fileName = link.Segments[4].TrimEnd('/') + "_" + link.Segments[3].TrimEnd('/') + ".html";
            }

            if (racecard != null)
            {
                try
                {
                    String folder = Utils.GetMyDocumentsFolderPath() + Globals.FOLDER_RACECARD;
                    String path = folder + fileName;
                    if (!Directory.Exists(folder))
                        Directory.CreateDirectory(folder);

                    doc2save.DocumentNode.AppendChild(racecard);
                    doc2save.Save(path);

                    return path;
                }
                catch (Exception e)
                {
                    throw new Exception(e.Message);
                }
            }

            return "";
        }

        public List<race_card> GetRaceCards(String rcPath)
        {
            List<race_card> ret = new List<race_card>();

            try
            {             
                var doc = new HtmlAgilityPack.HtmlDocument();

                doc.Load(rcPath);

                HtmlNodeCollection raceNodes = scraper.GetAllRaceNodes(doc);
                foreach (HtmlNode node in raceNodes)
                {                    
                    race_card rc = new race_card();

                    //populate general race info
                    rc.id = int.MaxValue;
                    rc.race_time = scraper.GetRaceTime(node);
                    rc.race_name = scraper.GetRaceName(node);
                    rc.race_restrictions_age = scraper.GetAgeRestriction(node);
                    rc.race_class = scraper.GetRaceClass(node);
                    rc.race_distance = (decimal)scraper.GetDistance(node);
                    rc.race_prize_money = scraper.GetPrizeMoney(node);
                    rc.race_going = scraper.GetGoingDescription(node);
                    rc.race_number_of_runners = scraper.GetNumberOfRunners(node);

                    //populate starters info
                    bool isJump = false;
                    int starterCnt = 0;
                    HtmlNodeCollection starterNodes = scraper.GetAllStarterNodes(node);
                    ObservableCollection<starter_info> starters = new ObservableCollection<starter_info>();
                    foreach (HtmlNode sNode in starterNodes)
                    {
                        starter_info st = new starter_info();
                        st.horse_name = scraper.GetHorseName(sNode);
                        st.stall = scraper.GetStall(sNode);

                        //this is the time to check if this is a flat or jump race
                        if (st.stall == 0)
                        {
                            isJump = true;
                            break;
                        }

                        st.horse_age = scraper.GetHorseAge(sNode);
                        st.trainer_name = new KeyValuePair<int, String>(scraper.GetTrainerName<int>(sNode), scraper.GetTrainerName<String>(sNode));
                        st.jockey_name = new KeyValuePair<int, String>(scraper.GetJockeyName<int>(sNode), scraper.GetJockeyName<String>(sNode));
                        st.jockeys_claim = scraper.GetJockeyClaim(sNode);
                        st.pounds = scraper.GetWeightCarried(sNode);
                        st.odds = scraper.GetOdds(sNode);
                        st.is_favourite = scraper.GetFavouritismStatus(sNode);
                        st.verdict = BetVerdict.NO_BET;
                        st.oddsMovement = OddsMovement.UNCHANGED;

                        starters.Add(st);
                        starterCnt++;

                        if (starterCnt == rc.race_number_of_runners)
                            break;
                    }

                    if (isJump)
                        continue;

                    rc.starters = starters;

                    ret.Add(rc);
                }


            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }

            return ret;
        }
    }
}
