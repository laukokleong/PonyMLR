using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace PonyMLR.WebScraper
{
    public interface IRaceCardScraper<U, V>
        where U : class
        where V : class
    {
        //DateTime GetRaceDate(HtmlDocument doc);
        //TimeSpan GetRaceTime(HtmlDocument doc);
        //T GetRaceTrack<T>(HtmlDocument doc);
        //String GetRaceName(HtmlDocument doc);
        //String GetAgeRestriction(HtmlDocument doc);
        //int GetRaceClass(HtmlDocument doc);
        //float GetDistance(HtmlDocument doc);
        //int GetPrizeMoney(HtmlDocument doc);
        //String GetGoingDescription(HtmlDocument doc);
        //int GetNumberOfRunners(HtmlDocument doc);
        //String GetHorseName(HtmlNode node);
        //int GetStall(HtmlNode node);
        //int GetHorseAge(HtmlNode node);
        //T GetTrainerName<T>(HtmlNode node);
        //T GetJockeyName<T>(HtmlNode node);
        //int GetJockeyClaim(HtmlNode node);
        //int GetWeightCarried(HtmlNode node);
        //double GetOdds(HtmlNode node);
        //bool GetFavouritismStatus(HtmlNode node);

        DateTime GetRaceDate(U doc);
        TimeSpan GetRaceTime(U doc);
        T GetRaceTrack<T>(U doc);
        String GetRaceName(U doc);
        String GetAgeRestriction(U doc);
        int GetRaceClass(U doc);
        float GetDistance(U doc);
        int GetPrizeMoney(U doc);
        String GetGoingDescription(U doc);
        int GetNumberOfRunners(U doc);
        String GetHorseName(V node);
        int GetStall(V node);
        int GetHorseAge(V node);
        T GetTrainerName<T>(V node);
        T GetJockeyName<T>(V node);
        int GetJockeyClaim(V node);
        int GetWeightCarried(V node);
        double GetOdds(V node);
        bool GetFavouritismStatus(V node);
    }
}
