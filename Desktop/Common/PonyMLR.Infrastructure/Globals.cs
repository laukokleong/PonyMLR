using System;

namespace PonyMLR.Infrastructure
{
    public static class Globals
    {
        // files constant
        public const string FOLDER_MLR_COEFFICIENTS = @".\coefficients\";
        public const string FOLDER_MLR_CALCULATIONS = @"/calculations/";
        public const string FOLDER_RACECARD = @"/racecard/";

        // race constant
        public const double SEC_PER_LENGTH_GOOD = (1 / 5);
        public const double SEC_PER_LENGTH_SLOW = (1 / 6);
        public const double POUND_PER_LENGTH_5F = 3;
        public const double POUND_PER_LENGTH_6F = 2.5;
        public const double POUND_PER_LENGTH_7TO8F = 2;
        public const double POUND_PER_LENGTH_9TO10F = 1.75;
        public const double POUND_PER_LENGTH_11TO13F = 1.5;
        public const double POUND_PER_LENGTH_14F = 1.25;
        public const double POUND_PER_LENGTH_15F_UP = 1;
        public const int RATING_RANGE_PER_CLASS = 15;
        public const int HORSE_AVG_WEIGHT = 1100;

        // race type description
        public const string RACE_TYPE_HANDICAP = "Handicap";
        public const string RACE_TYPE_NURSERY = "Nursery";
        public const string RACE_TYPE_MAIDEN = "Maiden";
        public const string RACE_TYPE_NOVICE = "Novice";
        public const string RACE_TYPE_CONDITIONS_STAKES = "Conditions Stakes";
        public const string RACE_TYPE_CLASSIFIED_STAKES = "Classified Stakes";
        public const string RACE_TYPE_NOVICE_AUCTION = "Novice Auction";
        public const string RACE_TYPE_MAIDEN_HANDICAP = "Maiden Handicap";
        public const string RACE_TYPE_CLAIMING_STAKES = "Claiming Stakes";
        public const string RACE_TYPE_SELLING_STAKES = "Selling Stakes";

        // turf going description
        public const string TURF_GOING_HARD = "Hard";
        public const string TURF_GOING_FIRM = "Firm";
        public const string TURF_GOING_GOOD = "Good";
        public const string TURF_GOING_GOOD_TO_FIRM = "Good To Firm";
        public const string TURF_GOING_GOOD_TO_SOFT = "Good To Soft";
        public const string TURF_GOING_SOFT = "Soft";
        public const string TURF_GOING_HEAVY = "Heavy";

        // all weather going description
        public const string AW_GOING_FAST = "Fast";
        public const string AW_GOING_STANDARD = "Standard";
        public const string AW_GOING_STANDARD_TO_FAST = "Standard To Fast";
        public const string AW_GOING_STANDARD_TO_SLOW = "Standard To Slow";
        public const string AW_GOING_SLOW = "Slow";

        // race comment pointers
        public const string RACE_COMMENT_MADE_ALL = "made all";
        public const string RACE_COMMENT_MADE_MOST = "made most";
        public const string RACE_COMMENT_LED = "led";
        public const string RACE_COMMENT_WITH_LEADER = "with leader";
        public const string RACE_COMMENT_TRACKED_LEADER = "tracked leader";
        public const string RACE_COMMENT_TO_TRACK_LEADER = "to track leader";
        public const string RACE_COMMENT_TRACKING_LEADER = "tracking leader";
        public const string RACE_COMMENT_TRACK_LEADING = "track leading";
        public const string RACE_COMMENT_TRACKED_LEADING = "tracked leading";
        public const string RACE_COMMENT_PROMINENT = "prominent";
        public const string RACE_COMMENT_CHASED_LEADER = "chased leader";
        public const string RACE_COMMENT_TO_CHASE_LEADER = "to chase leader";
        public const string RACE_COMMENT_CHASING_LEADER = "chasing leader";
        public const string RACE_COMMENT_CHASE_LEADING = "chase leading";
        public const string RACE_COMMENT_CHASED_LEADING = "chased leading";
        public const string RACE_COMMENT_PRESSED_LEADER = "pressed leader";
        public const string RACE_COMMENT_TO_PRESS_LEADER = "to press leader";
        public const string RACE_COMMENT_PRESSING_LEADER = "pressing leader";
        public const string RACE_COMMENT_PRESS_LEADING = "press leading";
        public const string RACE_COMMENT_PRESSED_LEADING = "pressed leading";
        public const string RACE_COMMENT_IN_TOUCH = "in touch";
        public const string RACE_COMMENT_HELD_UP = "held up";
        public const string RACE_COMMENT_CLOSE_UP = "close up";
        public const string RACE_COMMENT_MIDFIELD = "midfield";
        public const string RACE_COMMENT_MID_DIVISION = "mid-division";
        public const string RACE_COMMENT_BEHIND = "behind";
        public const string RACE_COMMENT_REAR = "rear";
        public const string RACE_COMMENT_LAST = "last";

        // early speed figure
        public const double EARLY_SPEED_FIGURE_VERY_FAST = 5;
        public const double EARLY_SPEED_FIGURE_FAST = 4;
        public const double EARLY_SPEED_FIGURE_NORMAL = 3;
        public const double EARLY_SPEED_FIGURE_SLOW = 2;
        public const double EARLY_SPEED_FIGURE_VERY_SLOW = 1;
        public const double EARLY_SPEED_FIGURE_UNKNOWN = 0;
        public const double EARLY_SPEED_FIGURE_NOT_RATED = -1;

        // math constant
        public const int PERCENTAGE_DIVIDER = 100;

        // date time constant
        public const int MILISECONDS_MULTIPLIER = 1000;
        public const int SECONDS_PER_MINUTE = 60;
        public const int ROUND_THE_CLOCK_HOURS = 12;
        public const string GMT_STANDARD_TIME = "GMT Standard Time";

        // undefined assignments
        public const double UNDEFINED_FINISHING = 0;
        public const double UNDEFINED_PERCENTAGE = 0;
        public const double UNDEFINED_RATE = -1;
        public const double UNDEFINED_FINISHING_TIME = 1;
        public const double UNDEFINED_ODDS = 999;

        // calculation definitions
        public const int RACE_DISTANCE_TOLERANCE = 1;
        public const int MAX_STARTER_PREVIOUS_RACES = 50;
        public const int MAX_JOCKEY_PREVIOUS_RACES = 50;
        public const int MAX_TRAINER_PREVIOUS_RACES = 50;
        public const int MAX_JTRSHIP_PREVIOUS_RACES = 50;
        public const int MAX_RACETRACK_PREVIOUS_RACES = 50;

        // database parameters
        public static string DbName = "";

        // calculation options
        public const string CALCULATE_PV_THIS_RACE_ONLY = "This race only";
        public const string CALCULATE_PV_CURRENT_MEETING = "Current meeting";
        public const string CALCULATE_PV_ALL_MEETINGS = "All meetings";

        // research panel parameters
        public const string RESEARCH_FILTER_NAME_COURSE = "Course";
        public const string RESEARCH_FILTER_NAME_DISTANCE = "Distance";
        public const string RESEARCH_FILTER_NAME_GOING = "Going";
        public const string RESEARCH_FILTER_NAME_CLASS = "Class";

        // staking plans parameters
        public const string EVEN_STAKE = "Even Stake";
        public const string FIXED_RATIO = "Fixed Ratio";
        public const string SQUARE_ROOT = "Square Root";
        public const string KELLY_FRACTION = "Kelly Fraction";

        // wager type parameters
        public const string WAGER_TYPE_WIN = "Win";
        public const string WAGER_TYPE_PLACE = "Place";
        public const string WAGER_TYPE_EACHWAY = "Each Way";

        // bet selection options
        public const string BET_CRITERIA_SELECT_ALL = "All";
        public const string BET_CRITERIA_SELECT_BY_EDGE = "Edge";
        public const string BET_CRITERIA_SELECT_BY_PROB = "Probability";
        public const string BET_CRITERIA_SELECT_BY_EDGE_AND_PROB = "Edge and Prob";

        // bet trigger type
        public const string TRIGGER_MLR_SD = "Std Deviation";
        public const string TRIGGER_CUSTOM = "Custom";
    }

    public enum BetVerdict
    {
        NO_BET,
        BACK,
        LAY
    }

    public enum OddsMovement
    {
        UNCHANGED,
        SHORTENING,
        DRIFTING
    }
}
