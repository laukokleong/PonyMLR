using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Text.RegularExpressions;
using PonyMLR.DataAccess;

namespace PonyMLR.Infrastructure
{
    public static class Utils
    {
        public static bool IsWinner(int pos)
        {
            if (pos == 1)
                return true;

            return false;
        }

        public static bool IsPlacer(int pos, int runners)
        {
            bool ret = false;

            switch(pos)
            {
                case 1:
                    ret = true;
                    break;
                case 2:
                    if (runners > 4)
                        ret = true;
                    break;
                case 3:
                    if (runners > 7)
                        ret = true;
                    break;
                case 4:
                    if (runners > 15)
                        ret = true;
                    break;
                default:
                    break;
            }

            return ret;
        }

        public static double GetPlaceOdds(double odds, int runners, bool ishandicap)
        {
            double ret = 0;

            if ((runners >= 2) && (runners <= 4))
            {
                ret = 0;
            }
            else if ((runners >= 5) && (runners <= 7))
            {
                ret = odds / 4;
            }
            else if ((runners >= 8) && (runners <= 11))
            {
                ret = odds / 5;
            }
            else if (runners >= 12)
            {
                if (ishandicap == true)
                    ret = odds / 4;
                else
                    ret = odds / 5;
            }

            return ret;
        }

        public static int GetRaceSurface(string going)
        {
            int ret = 1;

            switch (going)
            {
                case "Hard":
                case "Firm":
                case "Good":
                case "Good To Firm":
                case "Good To Soft":              
                case "Good to Firm":
                case "Good to Soft":
                case "Soft":
                case "Heavy":
                    ret = 1;
                    break;

                case "Fast": 
                case "Standard":
                case "Standard To Fast":               
                case "Standard To Slow":
                case "Standard to Fast":
                case "Standard to Slow":
                case "Slow":
                    ret = 0;
                    break;

                default: break;
            }
            
            // 1 is turf, 0 is all weather
            return ret;
        }

        public static string GoingDescriptionConstructor(string going)
        {
            string[] words = going.Split(' ');
            if (words.Count() <= 3)
                return going;

            string goingLower = going.ToLower();
            if (goingLower.Contains("hard") == true)
            {
                return Globals.TURF_GOING_HARD;
            }
            else if (goingLower.Contains("heavy") == true)
            {
                return Globals.TURF_GOING_HEAVY;
            }
            else if (goingLower.Contains("firm") == true)
            {
                if (goingLower.Contains("good") == true)
                    return Globals.TURF_GOING_GOOD_TO_FIRM;
                else
                    return Globals.TURF_GOING_FIRM;
            }
            else if (goingLower.Contains("soft") == true)
            {
                if (goingLower.Contains("good") == true)
                    return Globals.TURF_GOING_GOOD_TO_FIRM;
                else
                    return Globals.TURF_GOING_SOFT;
            }
            else if (goingLower.Contains("fast") == true)
            {
                if (goingLower.Contains("standard") == true)
                    return Globals.AW_GOING_STANDARD_TO_FAST;
                else
                    return Globals.AW_GOING_FAST;
            }
            else if (goingLower.Contains("slow") == true)
            {
                if (goingLower.Contains("standard") == true)
                    return Globals.AW_GOING_STANDARD_TO_SLOW;
                else
                    return Globals.AW_GOING_SLOW;
            }
            else
            {
                if (goingLower.Contains("good") == true)
                    return Globals.TURF_GOING_GOOD;
                else if (goingLower.Contains("standard") == true)
                    return Globals.AW_GOING_STANDARD;
            }

            return going;
        }

        public static int GoingDescriptionBinaryConverter(string going)
        {
            int ret = 1;

            switch (going)
            {
                case "Hard":
                case "Firm":
                case "Good To Firm":
                case "Good":
                case "Good To Soft":
                case "Fast":
                case "Standard To Fast":
                case "Standard":
                case "Standard To Slow":
                case "Good to Firm":
                case "Good to Soft":
                case "Standard to Fast":
                case "Standard to Slow":
                    ret = 1;
                    break;

                case "Soft":
                case "Heavy":
                case "Slow":
                    ret = 0;
                    break;

                default: break;
            }

            return ret;
        }

        public static bool RaceTypeComparator(string rtype, string rname)
        {
            bool ret = true;
            if (rtype == Globals.RACE_TYPE_HANDICAP || rtype == Globals.RACE_TYPE_MAIDEN || rtype == Globals.RACE_TYPE_NOVICE)
            {
                if (rname.Contains(Globals.RACE_TYPE_MAIDEN_HANDICAP) || rname.Contains(Globals.RACE_TYPE_NOVICE_AUCTION))
                    return false;
            }

            if (rname.Contains(rtype) == false)
                ret = false;

            return ret;
        }

        public static int GetNewmarketCourseKey(DateTime dt)
        {
            int ret;
            int month = dt.Month;
            if (month == 6 || month == 7 || month == 8)
                ret = 24;
            else
                ret = 25;

            return ret;
        }

        public static string RemoveStringInBracket(string name)
        {
            return Regex.Replace(name, "(\\([a-zA-Z]*\\))", "").Trim();
        }

        public static string GetStringInBracket(string name)
        {
            return Regex.Match(name, "(\\([a-zA-Z]*\\))").ToString().Trim();
        }

        public static T ConvertName<T>(String name, Dictionary<int, Tuple<String, String>> dict)
        {
            string fName1, mName1, lName1, fName2, mName2, lName2;
            string[] parts1;
            string[] parts2;
            string[] parts3;
            int key = 0;
            String comp;

            if (name.CompareTo("") == 0)
            {
                if (typeof(T) == typeof(String))
                    return (T)(object)"";
                else
                    return (T)(object)0;
            }
                        
            //direct compare with alternate name first          
            key = dict.FirstOrDefault(x => x.Value.Item2.Trim() == name.Trim()).Key;

            //not found, let's do it the hard way
            if (key == 0)
            {
                foreach (Tuple<String, String> item in dict.Values)
                {
                    comp = item.Item1;
                    mName1 = "";
                    mName2 = "";

                    try
                    {
                        //remove jockey claims in bracket
                        name = Regex.Replace(name, "(\\([0-9]\\))", "").Trim();
                        comp = Regex.Replace(comp, "(\\([0-9]\\))", "").Trim();

                        //remove jockey country in bracket
                        name = RemoveStringInBracket(name);
                        comp = RemoveStringInBracket(comp);

                        //remove name designation
                        name = Regex.Replace(name, "(Mrs?|Miss|Sir)", "").Trim();
                        comp = Regex.Replace(comp, "(Mrs?|Miss|Sir)", "").Trim();

                        //get first name (initial) and last name of input name
                        if (Regex.Match(name, "(,)").ToString().CompareTo("") == 0)
                        {
                            parts1 = name.Split(' ');
                            fName1 = parts1[parts1.GetLowerBound(0)].Substring(0, 1).Trim();
                            lName1 = parts1[parts1.GetUpperBound(0)].Trim();

                            //check if middle name exists
                            if (parts1.GetUpperBound(0) > 1)
                                mName1 = parts1[1].Substring(0, 1).Trim();
                        }
                        else
                        {
                            parts1 = name.Split(',');
                            parts3 = parts1[parts1.GetUpperBound(0)].Trim().Split(' ');
                            fName1 = parts3[parts3.GetLowerBound(0)].Substring(0, 1);

                            //check if middle name exists
                            if (parts3.GetUpperBound(0) > 0)
                                mName1 = parts3[1].Substring(0, 1).Trim();

                            parts3 = parts1[parts1.GetLowerBound(0)].Split(' ');
                            lName1 = parts3[parts3.GetUpperBound(0)].Trim();
                        }

                        //get first name (initial) and last name of comparator
                        if (Regex.Match(comp, "(,)").ToString().CompareTo("") == 0)
                        {
                            parts2 = comp.Split(' ');
                            fName2 = parts2[parts2.GetLowerBound(0)].Substring(0, 1).Trim();
                            lName2 = parts2[parts2.GetUpperBound(0)].Trim();

                            //check if middle name exists
                            if (parts2.GetUpperBound(0) > 1)
                                mName2 = parts2[1].Substring(0, 1).Trim();
                        }
                        else
                        {
                            parts2 = comp.Split(',');
                            parts3 = parts2[parts2.GetUpperBound(0)].Trim().Split(' ');
                            fName2 = parts3[parts3.GetLowerBound(0)].Substring(0, 1);

                            //check if middle name exists
                            if (parts3.GetUpperBound(0) > 0)
                                mName2 = parts3[1].Substring(0, 1).Trim();

                            parts3 = parts2[parts2.GetLowerBound(0)].Split(' ');
                            lName2 = parts3[parts3.GetUpperBound(0)].Trim();
                        }

                        fName1 = Regex.Replace(fName1, "(')", "").Trim();
                        lName1 = Regex.Replace(lName1, "(')", "").Trim();
                        fName2 = Regex.Replace(fName2, "(')", "").Trim();
                        lName2 = Regex.Replace(lName2, "(')", "").Trim();

                        if (fName1.ToLower().CompareTo(fName2.ToLower()) == 0 && lName1.ToLower().CompareTo(lName2.ToLower()) == 0)
                        {
                            if (mName1.CompareTo("") != 0 && mName2.CompareTo("") != 0)
                            {
                                if (mName1.ToLower().CompareTo(mName2.ToLower()) != 0)
                                    continue;
                            }

                            key = dict.FirstOrDefault(x => x.Value.Item1 == item.Item1).Key;
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        throw new Exception(e.Message);
                    }
                }
            }

            if (typeof(T) == typeof(int))
                return (T)(object)key;
            else            
                return (T)(object)dict[key].Item1;
        }

        public static int StoneToPound(String st)
        {           
            string[] parts = st.Split('-');
            if (parts.GetUpperBound(0) == 0)
            {
                return 140;
            }                  
            else
            {
                int s, p;
                if (int.TryParse(parts[0], out s) == true && int.TryParse(parts[1], out p) == true)
                {
                    return ((s * 14) + p);
                }
            }

            return 140;
        }

        public static double FractionalOddsToDecimal(String frac)
        {
        
            if (frac.CompareTo("") == 0)
                return Globals.UNDEFINED_ODDS;

            string[] parts = frac.Split('/');
            if (parts.GetUpperBound(0) == 0)
            {
                return Globals.UNDEFINED_ODDS;
            }
            else
            {
                parts[1] = Regex.Match(parts[1], "(\\d+)").ToString();
                int x, y;
                if (int.TryParse(parts[0], out x) == true && int.TryParse(parts[1], out y) == true)
                    return (double)((float)x / y);
            }

            return Globals.UNDEFINED_ODDS;
        }

        public static DateTime ConvertTime(DateTime time, string timezone)
        {
            TimeZoneInfo timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            if (TimeZoneInfo.Local == timeZoneInfo)
                return time;

            return TimeZoneInfo.ConvertTime(time, timeZoneInfo);
        }

        public static double MinSecToSeconds(String time)
        {                        
            if (time.CompareTo("") == 0)
                return 0;

            string[] parts;
            time = Regex.Replace(time, "( mins|m)", "").ToString();
            parts = time.Split(' ');

            if (parts.GetUpperBound(0) == 0)
                return 1;

            float min, sec;
            if (float.TryParse(parts[0].Trim(), out min) == true && float.TryParse(Regex.Replace(parts[1], "s", "").Trim(), out sec) == true)
                return (double)((min * 60) + sec);

            return 0;
        }

        public static string ToCsv<T>(string separator, IEnumerable<T> objectlist)
        {
            Type t = typeof(T);
            FieldInfo[] fields = t.GetFields();

            string header = String.Join(separator, fields.Select(f => f.Name).ToArray());

            StringBuilder csvdata = new StringBuilder();
            csvdata.AppendLine(header);

            foreach (var o in objectlist)
                csvdata.AppendLine(ToCsvFields(separator, fields, o));

            return csvdata.ToString();
        }

        public static string ToCsvFields(string separator, FieldInfo[] fields, object o)
        {
            StringBuilder linie = new StringBuilder();

            foreach (var f in fields)
            {
                if (linie.Length > 0)
                    linie.Append(separator);

                var x = f.GetValue(o);

                if (x != null)
                    linie.Append(x.ToString());
            }

            return linie.ToString();
        }

        public static string GetMyDocumentsFolderPath()
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"/PonyMLR/";

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            return folder;
        }

        public static double CommentToEarlySpeedFigure(string comment)
        {
            double ret = Globals.EARLY_SPEED_FIGURE_UNKNOWN;

            if ((comment == null) || (comment == ""))
                return ret;
            
            string[] comments = comment.Split(',');
            for (int i=0; i<3; i++)
            {
                try
                {
                    if (comments[i].ToLower().Contains(Globals.RACE_COMMENT_MADE_ALL) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_MADE_MOST) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_LED) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_WITH_LEADER))
                        ret = Globals.EARLY_SPEED_FIGURE_VERY_FAST;

                    if (comments[i].ToLower().Contains(Globals.RACE_COMMENT_TRACKED_LEADER) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_TO_TRACK_LEADER) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_TRACKING_LEADER) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_TRACK_LEADING) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_TRACKED_LEADING) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_PROMINENT))
                        ret = Globals.EARLY_SPEED_FIGURE_FAST;

                    if (comments[i].ToLower().Contains(Globals.RACE_COMMENT_CHASED_LEADER) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_TO_CHASE_LEADER) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_CHASING_LEADER) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_CHASE_LEADING) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_CHASED_LEADING) ||                      
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_PRESSED_LEADER) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_TO_PRESS_LEADER) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_PRESSING_LEADER) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_PRESS_LEADING) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_PRESSED_LEADING) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_IN_TOUCH))
                        ret = Globals.EARLY_SPEED_FIGURE_NORMAL;

                    if (comments[i].ToLower().Contains(Globals.RACE_COMMENT_HELD_UP) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_CLOSE_UP) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_MIDFIELD) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_MID_DIVISION))
                        ret = Globals.EARLY_SPEED_FIGURE_SLOW;

                    if (comments[i].ToLower().Contains(Globals.RACE_COMMENT_BEHIND) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_REAR) ||
                        comments[i].ToLower().Contains(Globals.RACE_COMMENT_LAST))
                        ret = Globals.EARLY_SPEED_FIGURE_VERY_SLOW;
                }
                catch (Exception) { }

                if (ret != 0)
                    break;
            }

            return ret; 
        }
    }
}
