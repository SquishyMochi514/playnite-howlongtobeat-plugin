using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HowLongToBeat.Models
{
    class HltbUserListJsonResponse
    {
        public class Data
        {
            public int count;
            public List<GamesList> gamesList;
            public int total;
            public List<PlatformList> platformList;
            public SummaryData summaryData;
        }

        public class GamesList
        {
            public int id;
            public string custom_title;
            public string platform;
            public string play_storefront;
            public int list_playing;
            public int list_backlog;
            public int list_replay;
            public int list_custom;
            public int list_custom2;
            public int list_custom3;
            public int list_comp;
            public int list_retired;
            public int comp_main;
            public int comp_plus;
            public int comp_100;
            public int comp_speed;
            public int comp_speed100;
            public string comp_main_notes;
            public string comp_plus_notes;
            public string comp_100_notes;
            public string comp_speed_notes;
            public string comp_speed100_notes;
            public int invested_pro;
            public int invested_sp;
            public int invested_spd;
            public int invested_co;
            public int invested_mp;
            public int play_count;
            public int review_score;
            public string review_notes;
            public string retired_notes;
            public string date_complete;
            public string date_updated;
            public string play_video;
            public string play_notes;
            public int game_id;
            public string game_image;
            public string game_type;
            public string release_world;
            public int comp_all;
            public int comp_main_g;
            public int review_score_g;
        }

        public class PlatformList
        {
            public string platform;
            public int count_total;
        }

        public class Root
        {
            public Data data;
        }

        public class SummaryData
        {
            public int playCount;
            public int dlcCount;
            public int reviewTotal;
            public int reviewCount;
            public int totalPlayedSp;
            public int totalPlayedMp;
            public int toBeatListed;
            public int uniqueGameCount;
        }

    }
}
