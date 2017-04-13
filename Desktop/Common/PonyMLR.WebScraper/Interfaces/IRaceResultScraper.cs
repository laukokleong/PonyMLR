using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace PonyMLR.WebScraper
{
    public interface IRaceResultScraper
    {
        double GetCompletionTime(HtmlDocument doc);
        int GetFinishingPosition(HtmlNode node, int runners);
        double GetDistanceBeaten(HtmlNode node);
    }
}
