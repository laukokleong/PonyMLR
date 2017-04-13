using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using PonyMLR.Infrastructure;
using PonyMLR.DataAccess;
using PonyMLR.WebScraper;
using HtmlAgilityPack;

namespace PonyMLR.Modules.Manage
{
    public class WebDataUpdater
    {
        private SportingLifeResults scraper;
        private UnitOfWork uow = new UnitOfWork(Globals.DbName.ToLower());

        public WebDataUpdater(DateTime dt,
                            Dictionary<int, String> trackDict,
                            UnitOfWork uow,
                            Dictionary<int, Tuple<String, String>> trainerDict,
                            Dictionary<int, Tuple<String, String>> jockeyDict)
        {
            String dtStr = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            scraper = new SportingLifeResults(dtStr, trackDict, trainerDict, jockeyDict);

            this.uow = uow;
        }

        public List<Uri> GetResultsUrl()
        {
            return scraper.ResultUrls;
        }

        public race_info GetRaceInfo(Uri url, int raceId)
        {
            race_info ret = new race_info();

            ret.race_id = raceId;

            try
            {
                HttpWebRequest oReq = (HttpWebRequest)WebRequest.Create(url);
                HttpWebResponse resp = (HttpWebResponse)oReq.GetResponse();

                var doc = new HtmlAgilityPack.HtmlDocument();

                doc.Load(resp.GetResponseStream());

                //get race date
                ret.race_date = scraper.GetRaceDate(doc);
                //get race time
                ret.race_time = scraper.GetRaceTime(doc);
                //get race track
                ret.track_key = scraper.GetRaceTrack<int>(doc);
                //get race name
                ret.race_name = scraper.GetRaceName(doc);
                //get age restriction
                ret.race_restrictions_age = scraper.GetAgeRestriction(doc);
                //get race class
                ret.race_class = scraper.GetRaceClass(doc);
                //get race distance
                ret.race_distance = (decimal)scraper.GetDistance(doc);
                //get prize money
                ret.race_prize_money = scraper.GetPrizeMoney(doc);
                //get going description
                ret.race_going = scraper.GetGoingDescription(doc);
                //get number of runners
                ret.race_number_of_runners = scraper.GetNumberOfRunners(doc);
                //get finishing time
                ret.race_finishing_time = (decimal)scraper.GetCompletionTime(doc);
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }

            return ret;
        }

        public List<race_result> GetRaceResults(Uri url, int raceId)
        {
            List<race_result> ret = new List<race_result>();

            try
            {
                HttpWebRequest oReq = (HttpWebRequest)WebRequest.Create(url);
                HttpWebResponse resp = (HttpWebResponse)oReq.GetResponse();

                var doc = new HtmlAgilityPack.HtmlDocument();

                doc.Load(resp.GetResponseStream());
                int runners = scraper.GetNumberOfRunners(doc);

                double totalDb = 0;
                HtmlNodeCollection entryNodes = scraper.GetAllHorseNodes(doc);
                foreach (HtmlNode entryNode in entryNodes)
                {
                    race_result res = new race_result();
                    res.race_key = raceId;
                    //get finishing position
                    res.finishing_position = scraper.GetFinishingPosition(entryNode, runners);
                    //get distance beaten
                    res.distance_beaten = (decimal)(scraper.GetDistanceBeaten(entryNode) + totalDb);
                    totalDb = (double)res.distance_beaten;
                    //get winner bool
                    res.is_winner = Utils.IsWinner((int)res.finishing_position);
                    //get placer bool
                    res.is_placer = Utils.IsPlacer((int)res.finishing_position, runners);
                    //get stall
                    res.stall = scraper.GetStall(entryNode);
                    //if stall is zero, most probably this is a neither a flat nor an aw race
                    if (res.stall == 0)
                        continue;

                    //get horse key, add new horse if does not exist
                    string hname = scraper.GetHorseName(entryNode);
                    int id = GetHorseId(scraper.GetHorseName(entryNode));
                    if (id != 0)
                    {
                        res.horse_key = id;
                    }
                    else
                    {
                        int newId = uow.HorseInfoRepository.Get().Max(x => x.horse_id) + 1;
                        horse_info hinfo = new horse_info { horse_id = newId, horse_name = hname, };
                        uow.HorseInfoRepository.Insert(hinfo);
                        uow.Save();

                        res.horse_key = newId;
                    }
                    //get horse age
                    res.horse_age = scraper.GetHorseAge(entryNode);                   
                    //get trainer key
                    res.trainer_key = scraper.GetTrainerName<int>(entryNode);
                    //get jockey key
                    res.jockey_key = scraper.GetJockeyName<int>(entryNode);
                    //get jockey claims
                    res.jockeys_claim = scraper.GetJockeyClaim(entryNode);
                    //get weight carried
                    res.pounds = scraper.GetWeightCarried(entryNode);
                    //get odds
                    res.odds = (decimal)scraper.GetOdds(entryNode);
                    //get favouritism status
                    res.is_favourite = scraper.GetFavouritismStatus(entryNode);
                    //get race comment
                    res.race_comment = scraper.GetRaceComment(entryNode);

                    ret.Add(res);
                }


            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }

            return ret;
        }

        private int GetHorseId(string name)
        {
            int id = 0;

            try
            {
                string brname = Utils.RemoveStringInBracket(name);
                List<horse_info> hil = new List<horse_info>(uow.HorseInfoRepository.Get(x => x.horse_name.ToLower().Replace("'", "").Contains(brname.ToLower().Replace("'", "")) == true)
                                                                .Where(y=>Utils.RemoveStringInBracket(y.horse_name.ToLower().Replace("'", "")).Equals(brname.ToLower().Replace("'", "")) == true)
                                                                );
                if (hil.Count() > 1)
                {
                    string bracketedstr = Utils.GetStringInBracket(name);
                    foreach (horse_info hi in hil)
                    {
                        if (bracketedstr == Utils.GetStringInBracket(hi.horse_name))
                            return hi.horse_id;
                    }
                }
                else
                {
                    return hil.LastOrDefault().horse_id;
                }
             
            }
            catch (Exception) { }

            return id;
        }
    }
}