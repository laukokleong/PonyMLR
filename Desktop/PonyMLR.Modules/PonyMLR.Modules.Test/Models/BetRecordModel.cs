using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PonyMLR.Modules.Test
{
    public class BetRecordModel
    {
        public DateTime race_date { get; set; }
        public TimeSpan race_time { get; set; }
        public double race_distance { get; set; }
        public string racetrack { get; set; }
        public string mlr_version { get; set; }
        public string description { get; set; }
        public int horse_age { get; set; }
        public double horse_levelstake_p { get; set; }
        public double hpoverrace { get; set; }
        public double fair_odds { get; set; }
        public double actual_odds { get; set; }
        public double edge { get; set; }
        public double stake { get; set; }
        public double profit { get; set; }
        public double balance { get; set; }
        public double early_speed { get; set; }
        public double averageEs { get; set; }
        public string esString { get; set; }
        public bool is_win { get; set; }
        public bool is_place { get; set; }
    }

    public class FavStatModel
    {
        public string race_date { get; set; }
        public int race_count { get; set; }
        public int fav_win_cnt { get; set; }
        public double fav_win_percentage { get; set; }
    }
}
