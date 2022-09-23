﻿using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using HowLongToBeat.Models;
using HowLongToBeat.Views;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Text.RegularExpressions;
using System.IO;
using CommonPluginsShared;
using System.Threading;
using System.Reflection;
using AngleSharp.Dom;
using CommonPluginsShared.Converters;
using CommonPluginsShared.Extensions;
using System.Text;
using System.Web;
using Newtonsoft.Json;

namespace HowLongToBeat.Services
{
    public enum StatusType
    {
        Playing,
        Backlog,
        Replays,
        CustomTab,
        Completed,
        Retired
    }

    public enum TimeType
    {
        MainStory,
        MainStoryExtra,
        Completionist,
        solo,
        CoOp,
        Versus
    }


    public class HowLongToBeatClient : ObservableObject
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static IResourceProvider resources = new ResourceProvider();

        private static HowLongToBeatDatabase PluginDatabase = HowLongToBeat.PluginDatabase;

        protected static IWebView _WebViewOffscreen;
        internal static IWebView WebViewOffscreen
        {
            get
            {
                if (_WebViewOffscreen == null)
                {
                    _WebViewOffscreen = PluginDatabase.PlayniteApi.WebViews.CreateOffscreenView();
                }
                return _WebViewOffscreen;
            }

            set
            {
                _WebViewOffscreen = value;
            }
        }


        private const string UrlBase = "https://howlongtobeat.com/";

        private const string UrlLogin = UrlBase + "login";
        private const string UrlLogOut = UrlBase + "login?t=out";

        private const string UrlUser = UrlBase + "user/";
        private const string UrlUserStatsGameList = UrlBase + "api/user/{0}/games/list";
        private const string UrlUserStatsGameDetails = UrlBase + "user_games_detail";

        private const string UrlPostData = UrlBase + "submit";
        private const string UrlPostDataEdit = UrlBase + "submit?s=add&eid={0}";
        private const string UrlSearch = UrlBase + "api/search";

        private const string UrlGame = UrlBase + "game/{0}";
        private const string UrlGames = UrlBase + "games/{0}";

        private const string UrlExportAll = UrlBase + "user_export?all=1";


        private bool? _IsConnected = null;
        public bool? IsConnected
        {
            get
            {
                return _IsConnected;
            }
            set
            {
                _IsConnected = value;
                OnPropertyChanged();
            }
        }

        public string UserLogin = string.Empty;
        public int UserId = 0;
        public HltbUserStats hltbUserStats = new HltbUserStats();

        private bool IsFirst = true;


        public HowLongToBeatClient()
        {
            UserLogin = PluginDatabase.PluginSettings.Settings.UserLogin;
        }


        /// <summary>
        /// Convert Time string from hltb to long seconds.
        /// </summary>
        /// <param name="Time"></param>
        /// <returns></returns>
        private long ConvertStringToLong(string Time)
        {
            if (Time.IndexOf("Hours") > -1)
            {
                Time = Time.Replace("Hours", string.Empty);
                Time = Time.Replace("&#189;", ".5");
                Time = Time.Replace("½", ".5");
                Time = Time.Trim();

                return (long)(Convert.ToDouble(Time, new NumberFormatInfo { NumberGroupSeparator = "." }) * 3600);
            }

            if (Time.IndexOf("Mins") > -1)
            {
                Time = Time.Replace("Mins", string.Empty);
                Time = Time.Replace("&#189;", ".5");
                Time = Time.Replace("½", ".5");
                Time = Time.Trim();

                return (long)(Convert.ToDouble(Time, new NumberFormatInfo { NumberGroupSeparator = "." }) * 60);
            }

            return 0;
        }

        private long ConvertStringToLongUser(string Time)
        {
            long.TryParse(Regex.Match(Time, @"\d+h").Value.Replace("h", string.Empty).Trim(), out long hours);
            long.TryParse(Regex.Match(Time, @"\d+m").Value.Replace("m", string.Empty).Trim(), out long minutes);
            long.TryParse(Regex.Match(Time, @"\d+s").Value.Replace("s", string.Empty).Trim(), out long secondes);

            long TimeConverted = hours * 3600 + minutes * 60 + secondes;

            Common.LogDebug(true, $"ConvertStringToLongUser: {Time.Trim()} - {TimeConverted}");

            return TimeConverted;
        }


        #region Search
        public List<HltbDataUser> Search(string Name, string Platform = "")
        {
            string data = GameSearch(Name, Platform).GetAwaiter().GetResult();
            List<HltbDataUser> dataParsed = SearchParser(Serialization.FromJson<dynamic>(data));
            return dataParsed;
        }

        /// <summary>
        /// Download search data.
        /// </summary>
        /// <param name="Name"></param>
        /// <returns></returns>
        private async Task<string> GameSearch(string Name, string Platform = "")
        {
            try
            {
                List<Playnite.SDK.HttpCookie> Cookies = WebViewOffscreen.GetCookies();
                Cookies = Cookies.Where(x => x != null && x.Domain != null && x.Domain.Contains("howlongtobeat", StringComparison.InvariantCultureIgnoreCase)).ToList();

                dynamic content = new {
                    searchType = "games",
                    searchTerms = Name.Split(' '),
                    searchPage= 1,
                    size=20,
                    searchOptions = new {games= new {userId = hltbUserStats.UserId, platform=Platform,sortCategory= "popular", rangeCategory="main",rangeTime= new {min=0,max=0},gameplay=new {perspective= "",flow="",genre=""},modifier=""},users= new{sortCategory= "postcount"},filter="",sort=0,randomizer=0}};


                var response = string.Empty;

                HttpClientHandler handler = new HttpClientHandler();
                if (Cookies != null)
                {
                    CookieContainer cookieContainer = new CookieContainer();

                    foreach (var cookie in Cookies)
                    {
                        Cookie c = new Cookie();
                        c.Name = cookie.Name;
                        c.Value = cookie.Value;
                        c.Domain = cookie.Domain;
                        c.Path = cookie.Path;

                        try
                        {
                            cookieContainer.Add(c);
                        }
                        catch (Exception ex)
                        {
                            Common.LogError(ex, true);
                        }
                    }

                    handler.CookieContainer = cookieContainer;
                }

                using (var client = new HttpClient(handler))
                {
                    client.DefaultRequestHeaders.Add("User-Agent", Web.UserAgent);
                    client.DefaultRequestHeaders.Add("accept", "application/json, text/javascript, */*; q=0.01");
                    client.DefaultRequestHeaders.Add("referer", UrlUser + UserLogin+"?q="+ HttpUtility.UrlEncode(Name)+"Accept-Encoding");
                    HttpContent c = new StringContent(Serialization.ToJson(content), Encoding.UTF8, "application/json");

                    HttpResponseMessage result;
                    try
                    {
                        result = await client.PostAsync(UrlSearch, c).ConfigureAwait(false);
                        if (result.IsSuccessStatusCode)
                        {
                            response = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            logger.Error($"Web error with status code {result.StatusCode.ToString()}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, $"Error on Post {UrlSearch}");
                    }
                }

                //settings.HttpWebRequest.UseUnsafeHeaderParsing = defaultValue;
                //config.Save(ConfigurationSaveMode.Modified);
                //ConfigurationManager.RefreshSection("system.net/settings");

                return response;

    
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
                return string.Empty;
            }
        }

        public GameHowLongToBeat SearchData(Game game)
        {
            Common.LogDebug(true, $"Search data for {game.Name}");

            if (PluginDatabase.PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                HowLongToBeatSelect ViewExtension = null;
                Application.Current.Dispatcher.BeginInvoke((Action)delegate
                {
                    ViewExtension = new HowLongToBeatSelect(null, game);
                    Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(PluginDatabase.PlayniteApi, resources.GetString("LOCSelection"), ViewExtension);
                    windowExtension.ShowDialog();
                }).Wait();

                if (ViewExtension.gameHowLongToBeat?.Items.Count > 0)
                {
                    return ViewExtension.gameHowLongToBeat;
                }
            }
            return null;
        }
        

        /// <summary>
        /// Parse html search result.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private List<HltbDataUser> SearchParser(dynamic data)
        {
            List<HltbDataUser> ReturnData = new List<HltbDataUser>();

            if (data != null)
            {              

                        foreach (var title in data.data)
                        {
                            ReturnData.Add(new HltbDataUser
                            {
                                Name = title.game_name,
                                Id = title.game_id,
                                UrlImg = string.Format(UrlGames, title.game_image)+"?width=250",
                                Url = string.Format(UrlGame, title.game_id),
                                GameHltbData = new HltbData
                                {
                                    MainStory = title.comp_lvl_combine == 0?title.comp_main:0,
                                    MainExtra = title.comp_lvl_combine == 0 ? title.comp_plus:0,
                                    Completionist = title.comp_lvl_combine == 0 ? title.comp_100:0,                                   
                                    Solo = (title.comp_lvl_combine == 1 && title.comp_lvl_sp == 1)? title.comp_all : 0,
                                    CoOp = (title.comp_lvl_combine == 1 && title.comp_lvl_co == 1) ? title.invested_co : 0,
                                    Vs = (title.comp_lvl_combine == 1 && title.comp_lvl_mp == 1) ? title.invested_mp : 0,
                                }
                            });
                        }                       
                    
               
            }

            return ReturnData;
        }
        #endregion


        #region user account
        public bool GetIsUserLoggedIn()
        {
            if (UserId == 0)
            {
                UserId = HowLongToBeat.PluginDatabase.Database.UserHltbData.UserId;
            }

            if (UserId == 0)
            {
                IsConnected = false;
                return false;
            }

            if (IsConnected == null)
            {
                WebViewOffscreen.NavigateAndWait(UrlBase);
                IsConnected = WebViewOffscreen.GetPageSource().ToLower().IndexOf("log in") == -1;
            }

            IsConnected = (bool)IsConnected;
            return !!(bool)IsConnected;
        }

        public void Login()
        {
            Application.Current.Dispatcher.BeginInvoke((Action)delegate
            {
                logger.Info("Login()");
                IWebView WebView = PluginDatabase.PlayniteApi.WebViews.CreateView(490, 670);
                WebView.LoadingChanged += (s, e) =>
                {
                    Common.LogDebug(true, $"NavigationChanged - {WebView.GetCurrentAddress()}");

                    if (WebView.GetCurrentAddress().StartsWith("https://howlongtobeat.com/user/"))
                    {
                        UserLogin = WebUtility.HtmlDecode(WebView.GetCurrentAddress().Replace("https://howlongtobeat.com/user/", string.Empty));
                        IsConnected = true;

                        PluginDatabase.PluginSettings.Settings.UserLogin = UserLogin;

                        Thread.Sleep(1500);
                        WebView.Close();
                    }
                };

                IsConnected = false;
                WebView.Navigate(UrlLogOut);
                WebView.Navigate(UrlLogin);
                WebView.OpenDialog();
            }).Completed += (s, e) => 
            {
                if ((bool)IsConnected)
                {
                    Application.Current.Dispatcher?.BeginInvoke((Action)delegate
                    {
                        try
                        {
                            PluginDatabase.Plugin.SavePluginSettings(PluginDatabase.PluginSettings.Settings);

                            Task.Run(() => {
                                UserId = GetUserId();
                                HowLongToBeat.PluginDatabase.RefreshUserData();
                            });
                        }
                        catch (Exception ex)
                        {
                            Common.LogError(ex, false, true, PluginDatabase.PluginName);
                        }
                    });
                }
            };
        }


        private Dictionary<string, DateTime> GetListGameWithDateUpdate()
        {
            //string webData = GetUserGamesList(true);
            string webData = "";
            HtmlParser parser = new HtmlParser();
            IHtmlDocument htmlDocument = parser.Parse(webData);

            Dictionary<string, DateTime> data = new Dictionary<string, DateTime>();
            foreach (IElement ListGame in htmlDocument.QuerySelectorAll("table.user_game_list tbody"))
            {
                IHtmlCollection<IElement> tr = ListGame.QuerySelectorAll("tr");
                IHtmlCollection<IElement> td = tr[0].QuerySelectorAll("td");

                string UserGameId = ListGame.GetAttribute("id").Replace("user_sel_", string.Empty).Trim();
                string sDateTime = td[1].InnerHtml;
                DateTime.TryParseExact(sDateTime, "MMM dd, yyyy", new CultureInfo("en-US"), DateTimeStyles.None, out DateTime dateTime);

                if (dateTime == default(DateTime))
                {
                    DateTime.TryParseExact(sDateTime, "MMMM dd, yyyy", new CultureInfo("en-US"), DateTimeStyles.None, out dateTime);
                }

                data.Add(UserGameId, dateTime);
            }

            return data;
        }

        private int GetUserId()
        {
            try
            {
                List<Playnite.SDK.HttpCookie> Cookies = WebViewOffscreen.GetCookies();
                Cookies = Cookies.Where(x => x != null && x.Domain != null && x.Domain.Contains("howlongtobeat", StringComparison.InvariantCultureIgnoreCase)).ToList();                        

                string response = Web.DownloadStringData("https://howlongtobeat.com/api/user", Cookies).GetAwaiter().GetResult();
                var t = Serialization.FromJson<dynamic>(response);
                return t.data[0].user_id;
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
                return 0;
            }
        }

        private async Task<HltbUserListJsonResponse> GetUserGamesListAsync(bool WithDateUpdate = false)
        {
            try
            {
                List<Playnite.SDK.HttpCookie> Cookies = WebViewOffscreen.GetCookies();
                Cookies = Cookies.Where(x => x != null && x.Domain != null && x.Domain.Contains("howlongtobeat", StringComparison.InvariantCultureIgnoreCase)).ToList();

                string lists = "completed playing backlog replays custom custom2 custom3";


                dynamic content = new
                {
                    user_id = hltbUserStats.UserId,
                    lists = lists.Split(' '),
                    currentUserHome = true,
                    limit = 1000,
                    name = "",
                    platform = "",
                    set_playstyle = "comp_main",
                    sortBy = "",
                    sortFlip = 0,
                    storefront = "",
                    view = "",
                };


                var response = string.Empty;
                HltbUserListJsonResponse responseObject = new HltbUserListJsonResponse();

                HttpClientHandler handler = new HttpClientHandler();
                if (Cookies != null)
                {
                    CookieContainer cookieContainer = new CookieContainer();

                    foreach (var cookie in Cookies)
                    {
                        Cookie c = new Cookie();
                        c.Name = cookie.Name;
                        c.Value = cookie.Value;
                        c.Domain = cookie.Domain;
                        c.Path = cookie.Path;

                        try
                        {
                            cookieContainer.Add(c);
                        }
                        catch (Exception ex)
                        {
                            Common.LogError(ex, true);
                        }
                    }

                    handler.CookieContainer = cookieContainer;
                }

                using (var client = new HttpClient(handler))
                {
                    client.DefaultRequestHeaders.Add("User-Agent", Web.UserAgent);
                    client.DefaultRequestHeaders.Add("accept", "application/json, text/javascript, */*; q=0.01");
                    client.DefaultRequestHeaders.Add("referer", UrlUser + UserLogin + "?q=" + HttpUtility.UrlEncode(hltbUserStats.Login) + "Accept-Encoding");
                    HttpContent c = new StringContent(Serialization.ToJson(content), Encoding.UTF8, "application/json");

                    var urlGameListWithId = string.Format(UrlUserStatsGameList, hltbUserStats.UserId);
                    
                    HttpResponseMessage result;
                    try
                    {
                        
                        result = await client.PostAsync(urlGameListWithId, c).ConfigureAwait(false);
                        if (result.IsSuccessStatusCode)
                        {
                            response = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
                            responseObject = JsonConvert.DeserializeObject<HltbUserListJsonResponse>(response);
                        }
                        else
                        {
                            logger.Error($"Web error with status code {result.StatusCode.ToString()}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, $"Error on Post {urlGameListWithId}");
                    }
                }

                //settings.HttpWebRequest.UseUnsafeHeaderParsing = defaultValue;
                //config.Save(ConfigurationSaveMode.Modified);
                //ConfigurationManager.RefreshSection("system.net/settings");

                return responseObject;
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
                return new HltbUserListJsonResponse();
            }
        }

        private string GetUserGamesDetail(string UserGameId)
        {
            try
            {
                List<Playnite.SDK.HttpCookie> Cookies = WebViewOffscreen.GetCookies();
                Cookies = Cookies.Where(x => x != null && x.Domain != null && x.Domain.Contains("howlongtobeat", StringComparison.InvariantCultureIgnoreCase)).ToList();

                FormUrlEncodedContent formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("option", UserGameId),
                    new KeyValuePair<string, string>("option_b", "comp_all")
                });

                string response = Web.PostStringDataCookies(UrlUserStatsGameDetails, formContent, Cookies).GetAwaiter().GetResult();
                return response;
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
                return string.Empty;
            }
        }

        private TitleList GetTitleList(IElement element)
        {
            try
            {
                TitleList titleList = new TitleList();

                IHtmlCollection<IElement> tr = element.QuerySelectorAll("tr");
                IHtmlCollection<IElement> td = tr[0].QuerySelectorAll("td");

                titleList.UserGameId = element.GetAttribute("id").Replace("user_sel_", string.Empty).Trim();
                titleList.GameName = WebUtility.HtmlDecode(td[0].QuerySelector("a").InnerHtml.Trim());
                titleList.Platform = WebUtility.HtmlDecode(td[0].QuerySelector("span").InnerHtml.Trim());
                titleList.Id = int.Parse(td[0].QuerySelector("a").GetAttribute("href").Replace("game?id=", string.Empty));

                string sCurrentTime = td[1].InnerHtml;
                titleList.CurrentTime = ConvertStringToLongUser(sCurrentTime);

                HltbPostData hltbPostData = GetSubmitData(titleList.GameName, titleList.UserGameId);
                if (hltbPostData != null)
                {
                    string tempCurrentTime = (hltbPostData.protime_h.IsNullOrEmpty()) ? string.Empty : hltbPostData.protime_h + "h";
                    tempCurrentTime += (hltbPostData.protime_m.IsNullOrEmpty()) ? string.Empty : " " + hltbPostData.protime_m + "m";
                    tempCurrentTime += (hltbPostData.protime_s.IsNullOrEmpty()) ? string.Empty : " " + hltbPostData.protime_s + "s";

                    titleList.CurrentTime = ConvertStringToLongUser(tempCurrentTime.Trim());

                    titleList.IsReplay = (hltbPostData.play_num == 2);
                    titleList.IsRetired = (hltbPostData.list_rt == "1");
                }

                string response = GetUserGamesDetail(titleList.UserGameId);
                if (response.IsNullOrEmpty())
                {
                    logger.Warn($"No details for {titleList.GameName}");
                    return null;
                }

                HtmlParser parser = new HtmlParser();
                IHtmlDocument htmlDocument = parser.Parse(response);

                IHtmlCollection<IElement> GameDetails = htmlDocument.QuerySelectorAll("div.user_game_detail > div");

                // Game status type
                titleList.GameStatuses = new List<GameStatus>();
                foreach (IElement GameStatus in GameDetails[0].QuerySelectorAll("span"))
                {
                    switch (GameStatus.InnerHtml.ToLower())
                    {
                        case "playing":
                            titleList.GameStatuses.Add(new GameStatus { Status = StatusType.Playing });
                            break;
                        case "backlog":
                            titleList.GameStatuses.Add(new GameStatus { Status = StatusType.Backlog });
                            break;
                        case "replays":
                            titleList.GameStatuses.Add(new GameStatus { Status = StatusType.Replays });
                            break;
                        case "custom tab":
                            titleList.GameStatuses.Add(new GameStatus { Status = StatusType.CustomTab });
                            break;
                        case "completed":
                            titleList.GameStatuses.Add(new GameStatus { Status = StatusType.Completed });
                            break;
                        case "retired":
                            titleList.GameStatuses.Add(new GameStatus { Status = StatusType.Retired });
                            break;
                    }
                }

                // Game status time
                int iPosUserData = 1;
                if (GameDetails[1].InnerHtml.ToLower().Contains("<h5>progress</h5>"))
                {
                    List<string> ListTime = GameDetails[1].QuerySelector("span").InnerHtml
                        .Replace("<strong>", string.Empty).Replace("</strong>", string.Empty)
                        .Split('/').ToList();

                    for (int i = 0; i < titleList.GameStatuses.Count; i++)
                    {
                        titleList.GameStatuses[i].Time = ConvertStringToLongUser(ListTime[i]);
                    }

                    iPosUserData = 2;
                }

                // Storefront
                IElement elStorefront = htmlDocument.QuerySelectorAll("h5").Where(x => x.InnerHtml.ToLower().Contains("storefront")).FirstOrDefault();
                if (elStorefront != null)
                {
                    titleList.Storefront = WebUtility.HtmlDecode(elStorefront.ParentElement?.QuerySelector("div")?.InnerHtml?.Trim());
                }

                // Updated - sec - min - hour - day - week - month - year 
                IElement elUpdated = htmlDocument.QuerySelectorAll("h5").Where(x => x.InnerHtml.ToLower().Contains("updated")).FirstOrDefault();
                if (elUpdated != null)
                {
                    string dataUpdate = WebUtility.HtmlDecode(elUpdated.ParentElement?.QuerySelector("p")?.InnerHtml?.Trim());
                    string doubleString = Regex.Replace(dataUpdate, @"[^\d.\d]", string.Empty).Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
                    double.TryParse(doubleString, out double doubleData);

                    if (dataUpdate.Contains("sec", StringComparison.InvariantCultureIgnoreCase))
                    {
                        titleList.LastUpdate = DateTime.Now.AddSeconds(-1 * doubleData);
                    }

                    if (dataUpdate.Contains("min", StringComparison.InvariantCultureIgnoreCase))
                    {
                        titleList.LastUpdate = DateTime.Now.AddMinutes(-1 * doubleData);
                    }

                    if (dataUpdate.Contains("hour", StringComparison.InvariantCultureIgnoreCase))
                    {
                        titleList.LastUpdate = DateTime.Now.AddHours(-1 * doubleData);
                    }

                    if (dataUpdate.Contains("day", StringComparison.InvariantCultureIgnoreCase))
                    {
                        titleList.LastUpdate = DateTime.Now.AddDays(-1 * doubleData);
                    }

                    if (dataUpdate.Contains("week", StringComparison.InvariantCultureIgnoreCase))
                    {
                        doubleData = doubleData * 7;
                        titleList.LastUpdate = DateTime.Now.AddDays(-1 * doubleData);
                    }

                    if (dataUpdate.Contains("month", StringComparison.InvariantCultureIgnoreCase))
                    {
                        double days = (doubleData - (int)doubleData) * 30;
                        titleList.LastUpdate = DateTime.Now.AddMonths((int)(-1 * doubleData)).AddDays(days);
                    }

                    if (dataUpdate.Contains("year", StringComparison.InvariantCultureIgnoreCase))
                    {
                        titleList.LastUpdate = DateTime.Now.AddYears((int)(-1 * doubleData));
                    }
                }

                // User data
                titleList.HltbUserData = new HltbData();
                if (hltbPostData != null)
                {
                    // Completion date
                    string tempTime = hltbPostData.compyear + "-" + hltbPostData.compmonth + "-" + hltbPostData.compday;
                    if (DateTime.TryParse(tempTime, out DateTime dateValue))
                    {
                        titleList.Completion = Convert.ToDateTime(dateValue);
                    }
                    else
                    {
                        logger.Warn($"Impossible to parse datetime: {tempTime}");
                        titleList.Completion = null;
                    }

                    int.TryParse(hltbPostData.c_main_h, out int c_main_h);
                    int.TryParse(hltbPostData.c_main_m, out int c_main_m);
                    int.TryParse(hltbPostData.c_main_s, out int c_main_s);
                    titleList.HltbUserData.MainStory = c_main_h * 3600 + c_main_m * 60 + c_main_s;

                    int.TryParse(hltbPostData.c_plus_h, out int c_plus_h);
                    int.TryParse(hltbPostData.c_plus_m, out int c_plus_m);
                    int.TryParse(hltbPostData.c_plus_s, out int c_plus_s);
                    titleList.HltbUserData.MainExtra = c_plus_h * 3600 + c_plus_m * 60 + c_plus_s;

                    int.TryParse(hltbPostData.c_100_h, out int c_100_h);
                    int.TryParse(hltbPostData.c_100_m, out int c_100_m);
                    int.TryParse(hltbPostData.c_100_s, out int c_100_s);
                    titleList.HltbUserData.Completionist = c_100_h * 3600 + c_100_m * 60 + c_100_s;

                    int.TryParse(hltbPostData.cotime_h, out int cotime_h);
                    int.TryParse(hltbPostData.cotime_m, out int cotime_m);
                    int.TryParse(hltbPostData.cotime_s, out int cotime_s);
                    titleList.HltbUserData.CoOp = cotime_h * 3600 + cotime_m * 60 + cotime_s;

                    int.TryParse(hltbPostData.mptime_h, out int mptime_h);
                    int.TryParse(hltbPostData.mptime_m, out int mptime_m);
                    int.TryParse(hltbPostData.mptime_s, out int mptime_s);
                    titleList.HltbUserData.Vs = mptime_h * 3600 + mptime_m * 60 + mptime_s;
                }

                for (int i = 0; i < GameDetails[iPosUserData].Children.Count(); i++)
                {
                    if (GameDetails[iPosUserData].Children[i].InnerHtml.ToLower().Contains("solo"))
                    {
                        i++;
                        string tempTime = GameDetails[iPosUserData]?.Children[i]?.QuerySelector("span")?.InnerHtml;
                        titleList.HltbUserData.Solo = ConvertStringToLongUser(tempTime);
                    }
                }

                Common.LogDebug(true, $"titleList: {Serialization.ToJson(titleList)}");
                return titleList;
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
                return null;
            }
        }

        public HltbPostData GetSubmitData(string GameName, string UserGameId)
        {
            logger.Info($"GetSubmitData({GameName}, {UserGameId})");
            try
            {
                List<Playnite.SDK.HttpCookie> Cookies = WebViewOffscreen.GetCookies();
                Cookies = Cookies.Where(x => x != null && x.Domain != null && x.Domain.Contains("howlongtobeat", StringComparison.InvariantCultureIgnoreCase)).ToList();

                string response = Web.DownloadStringData(string.Format(UrlPostDataEdit, UserGameId), Cookies).GetAwaiter().GetResult();
                if (response.IsNullOrEmpty())
                {
                    logger.Warn($"No SubmitData for {GameName} - {UserGameId}");
                    return null;
                }

                HtmlParser parser = new HtmlParser();
                IHtmlDocument htmlDocument = parser.Parse(response);

                HltbPostData hltbPostData = new HltbPostData();

                IElement user_id = htmlDocument.QuerySelector("input[name=user_id]");
                int.TryParse(user_id.GetAttribute("value"), out int user_id_value);
                hltbPostData.user_id = user_id_value;

                IElement edit_id = htmlDocument.QuerySelector("input[name=edit_id]");
                int.TryParse(edit_id.GetAttribute("value"), out int edit_id_value);
                hltbPostData.edit_id = edit_id_value;

                IElement game_id = htmlDocument.QuerySelector("input[name=game_id]");
                int.TryParse(game_id.GetAttribute("value"), out int game_id_value);
                hltbPostData.game_id = game_id_value;


                if (hltbPostData.user_id == 0)
                {
                    throw new Exception($"No user_id for {GameName} - {UserGameId}");
                }
                if (hltbPostData.edit_id == 0)
                {
                    throw new Exception($"No edit_id for {GameName} - {UserGameId}");
                }
                if (hltbPostData.game_id == 0)
                {
                    throw new Exception($"No game_id for {GameName} - {UserGameId}");
                }


                IElement CustomTitle = htmlDocument.QuerySelector("input[name=custom_title]");
                hltbPostData.custom_title = CustomTitle.GetAttribute("value");

                // TODO No selected....
                IHtmlCollection<IElement> SelectPlatform = htmlDocument.QuerySelectorAll("select[name=platform]");
                foreach(IElement option in SelectPlatform[0].QuerySelectorAll("option"))
                {
                    if (option.GetAttribute("selected") == "selected")
                    {
                        hltbPostData.platform = option.InnerHtml;
                    }
                }
                if (hltbPostData.platform.IsNullOrEmpty())
                {
                    if (SelectPlatform.Count() > 1)
                    {
                        foreach (IElement option in SelectPlatform[1].QuerySelectorAll("option"))
                        {
                            if (option.GetAttribute("selected") == "selected")
                            {
                                hltbPostData.platform = option.InnerHtml;
                            }
                        }
                    }
                }


                IElement cbList = htmlDocument.QuerySelector("#list_p");
                if ((bool)cbList?.OuterHtml?.ToLower()?.Contains(" checked"))
                {
                    hltbPostData.list_p = "1";
                }

                cbList = htmlDocument.QuerySelector("#list_b");
                if (cbList != null && (bool)cbList?.OuterHtml?.ToLower()?.Contains(" checked"))
                {
                    hltbPostData.list_b = "1";
                }

                cbList = htmlDocument.QuerySelector("#list_r");
                if (cbList != null && (bool)cbList?.OuterHtml?.ToLower()?.Contains(" checked"))
                {
                    hltbPostData.list_r = "1";
                }

                cbList = htmlDocument.QuerySelector("#list_c");
                if (cbList != null && (bool)cbList?.OuterHtml?.ToLower()?.Contains(" checked"))
                {
                    hltbPostData.list_c = "1";
                }

                cbList = htmlDocument.QuerySelector("#list_cp");
                if (cbList != null && (bool)cbList?.OuterHtml?.ToLower()?.Contains(" checked"))
                {
                    hltbPostData.list_cp = "1";
                }

                cbList = htmlDocument.QuerySelector("#list_rt");
                if (cbList != null && (bool)cbList?.OuterHtml?.ToLower()?.Contains(" checked"))
                {
                    hltbPostData.list_rt = "1";
                }

                IElement cp_pull_h = htmlDocument.QuerySelector("#cp_pull_h");
                hltbPostData.protime_h = cp_pull_h.GetAttribute("value");

                IElement cp_pull_m = htmlDocument.QuerySelector("#cp_pull_m");
                hltbPostData.protime_m = cp_pull_m.GetAttribute("value");

                IElement cp_pull_s = htmlDocument.QuerySelector("#cp_pull_s");
                hltbPostData.protime_s = cp_pull_s.GetAttribute("value");


                IElement rt_notes = htmlDocument.QuerySelector("input[name=rt_notes]");
                hltbPostData.rt_notes = rt_notes.GetAttribute("value");


                IElement compmonth = htmlDocument.QuerySelector("#compmonth");
                foreach (IElement option in compmonth.QuerySelectorAll("option"))
                {
                    if (option.GetAttribute("selected") == "selected")
                    {
                        hltbPostData.compmonth = option.GetAttribute("value");
                    }
                }

                IElement compday = htmlDocument.QuerySelector("#compday");
                foreach (IElement option in compday.QuerySelectorAll("option"))
                {
                    if (option.GetAttribute("selected") == "selected")
                    {
                        hltbPostData.compday = option.GetAttribute("value");
                    }
                }

                IElement compyear = htmlDocument.QuerySelector("#compyear");
                foreach (IElement option in compyear.QuerySelectorAll("option"))
                {
                    if (option.GetAttribute("selected") == "selected")
                    {
                        hltbPostData.compyear = option.GetAttribute("value");
                    }
                }


                IElement play_num = htmlDocument.QuerySelector("#play_num");
                foreach (IElement option in play_num.QuerySelectorAll("option"))
                {
                    if (option.GetAttribute("selected") == "selected")
                    {
                        int.TryParse(option.GetAttribute("value"), out int play_num_value);
                        hltbPostData.play_num = play_num_value;
                    }
                }


                IElement c_main_h = htmlDocument.QuerySelector("#c_main_h");
                hltbPostData.c_main_h = c_main_h?.GetAttribute("value");

                IElement c_main_m = htmlDocument.QuerySelector("#c_main_m");
                hltbPostData.c_main_m = c_main_m?.GetAttribute("value");

                IElement c_main_s = htmlDocument.QuerySelector("#c_main_s");
                hltbPostData.c_main_s = c_main_s?.GetAttribute("value");

                IElement c_main_notes = htmlDocument.QuerySelector("input[name=c_main_notes]");
                hltbPostData.c_main_notes = c_main_notes?.GetAttribute("value");


                IElement c_plus_h = htmlDocument.QuerySelector("#c_plus_h");
                hltbPostData.c_plus_h = c_plus_h?.GetAttribute("value");

                IElement c_plus_m = htmlDocument.QuerySelector("#c_plus_m");
                hltbPostData.c_plus_m = c_plus_m?.GetAttribute("value");

                IElement c_plus_s = htmlDocument.QuerySelector("#c_plus_s");
                hltbPostData.c_plus_s = c_plus_s?.GetAttribute("value");

                IElement c_plus_notes = htmlDocument.QuerySelector("input[name=c_plus_notes]");
                hltbPostData.c_plus_notes = c_plus_notes?.GetAttribute("value");


                IElement c_100_h = htmlDocument.QuerySelector("#c_100_h");
                hltbPostData.c_100_h = c_100_h?.GetAttribute("value");

                IElement c_100_m = htmlDocument.QuerySelector("#c_100_m");
                hltbPostData.c_100_m = c_100_m?.GetAttribute("value");

                IElement c_100_s = htmlDocument.QuerySelector("#c_100_s");
                hltbPostData.c_100_s = c_100_s?.GetAttribute("value");

                IElement c_100_notes = htmlDocument.QuerySelector("input[name=c_100_notes]");
                hltbPostData.c_100_notes = c_100_notes?.GetAttribute("value");


                IElement c_speed_h = htmlDocument.QuerySelector("#c_speed_h");
                hltbPostData.c_speed_h = c_speed_h?.GetAttribute("value");

                IElement c_speed_m = htmlDocument.QuerySelector("#c_speed_m");
                hltbPostData.c_speed_m = c_speed_m?.GetAttribute("value");

                IElement c_speed_s = htmlDocument.QuerySelector("#c_speed_s");
                hltbPostData.c_speed_s = c_speed_s?.GetAttribute("value");

                IElement c_speed_notes = htmlDocument.QuerySelector("input[name=c_speed_notes]");
                hltbPostData.c_speed_notes = c_speed_notes?.GetAttribute("value");


                IElement cotime_h = htmlDocument.QuerySelector("#cotime_h");
                hltbPostData.cotime_h = cotime_h?.GetAttribute("value");

                IElement cotime_m = htmlDocument.QuerySelector("#cotime_m");
                hltbPostData.cotime_m = cotime_m?.GetAttribute("value");

                IElement cotime_s = htmlDocument.QuerySelector("#cotime_s");
                hltbPostData.cotime_s = cotime_s?.GetAttribute("value");


                IElement mptime_h = htmlDocument.QuerySelector("#mptime_h");
                hltbPostData.mptime_h = mptime_h?.GetAttribute("value");

                IElement mptime_m = htmlDocument.QuerySelector("#mptime_m");
                hltbPostData.mptime_m = mptime_m?.GetAttribute("value");

                IElement mptime_s = htmlDocument.QuerySelector("#mptime_s");
                hltbPostData.mptime_s = mptime_s?.GetAttribute("value");

                IElement mptime_notes = htmlDocument.QuerySelector("#mptime_notes");


                IElement review_score = htmlDocument.QuerySelector("select[name=review_score]");
                foreach (IElement option in review_score.QuerySelectorAll("option"))
                {
                    if (option.GetAttribute("selected") == "selected")
                    {
                        int.TryParse(option.GetAttribute("value"), out int review_score_value);
                        hltbPostData.review_score = review_score_value;
                    }
                }


                IElement review_notes = htmlDocument.QuerySelector("textarea[name=review_notes]");
                hltbPostData.review_notes = review_notes?.InnerHtml;

                IElement play_notes = htmlDocument.QuerySelector("textarea[name=play_notes]");
                hltbPostData.play_notes = play_notes?.InnerHtml;

                IElement play_video = htmlDocument.QuerySelector("input[name=play_video]");
                hltbPostData.play_video = play_video?.GetAttribute("value");


                return hltbPostData;
            }
            catch (Exception ex)
            {
                if (IsFirst)
                {
                    IsFirst = false;
                    return GetSubmitData(GameName, UserGameId);
                }
                else
                {
                    Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    return null;
                }
            }
        }


        public HltbUserStats LoadUserData()
        {
            string PathHltbUserStats = Path.Combine(PluginDatabase.Plugin.GetPluginUserDataPath(), "HltbUserStats.json");
            HltbUserStats hltbDataUser = new HltbUserStats();

            if (File.Exists(PathHltbUserStats))
            {
                try
                {
                    hltbDataUser = Serialization.FromJsonFile<HltbUserStats>(PathHltbUserStats);
                    hltbDataUser.TitlesList = hltbDataUser.TitlesList.Where(x => x != null).ToList();
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, true, PluginDatabase.PluginName);
                }
            }

            return hltbDataUser;
        }


        public HltbUserStats GetUserData()
        {
            if (GetIsUserLoggedIn())
            {
                hltbUserStats = new HltbUserStats();
                hltbUserStats.Login = (UserLogin.IsNullOrEmpty()) ? HowLongToBeat.PluginDatabase.Database.UserHltbData.Login : UserLogin;
                hltbUserStats.UserId = (UserId == 0) ? HowLongToBeat.PluginDatabase.Database.UserHltbData.UserId : UserId;
                hltbUserStats.TitlesList = new List<TitleList>();

                //string response = GetUserGamesList();
                HltbUserListJsonResponse response = GetUserGamesListAsync().GetAwaiter().GetResult();
                if (response == null)
                {
                    return null;
                }

                Dictionary<string, DateTime> ListGameWithDateUpdate = GetListGameWithDateUpdate();

                try
                {
                    HtmlParser parser = new HtmlParser();
                    IHtmlDocument htmlDocument = parser.Parse("");

                    foreach (IElement ListGame in htmlDocument.QuerySelectorAll("table.user_game_list tbody"))
                    {
                        TitleList titleList = GetTitleList(ListGame);
                        DateTime? dateUpdate = ListGameWithDateUpdate.Where(x => x.Key.IsEqual(titleList.UserGameId))?.FirstOrDefault().Value;
                        if (dateUpdate != null && (DateTime)dateUpdate != default(DateTime))
                        {
                            titleList.LastUpdate = (DateTime)dateUpdate;
                        }

                        hltbUserStats.TitlesList.Add(titleList);
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    return null;
                }

                return hltbUserStats;
            }
            else
            {
                PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                    $"{PluginDatabase.PluginName}-Import-Error",
                    PluginDatabase.PluginName + System.Environment.NewLine + resources.GetString("LOCCommonNotLoggedIn"),
                    NotificationType.Error,
                    () => PluginDatabase.Plugin.OpenSettingsView()
                ));
                return null;
            }
        }

        public TitleList GetUserData(int game_id)
        {
            if (GetIsUserLoggedIn())
            {
                //string response = GetUserGamesList();
                string response = "";
                if (response.IsNullOrEmpty())
                {
                    return null;
                }

                Dictionary<string, DateTime> ListGameWithDateUpdate = GetListGameWithDateUpdate();

                try
                {
                    HtmlParser parser = new HtmlParser();
                    IHtmlDocument htmlDocument = parser.Parse(response);

                    foreach (IElement ListGame in htmlDocument.QuerySelectorAll("table.user_game_list tbody"))
                    {
                        IHtmlCollection<IElement> tr = ListGame.QuerySelectorAll("tr");
                        IHtmlCollection<IElement> td = tr[0].QuerySelectorAll("td");

                        int Id = int.Parse(td[0].QuerySelector("a").GetAttribute("href").Replace("game?id=", string.Empty));

                        if (Id != game_id)
                        {
                            continue;
                        }

                        TitleList titleList = GetTitleList(ListGame);

                        DateTime? dateUpdate = ListGameWithDateUpdate.Where(x => x.Key.IsEqual(titleList.UserGameId))?.FirstOrDefault().Value;
                        if (dateUpdate != null)
                        {
                            titleList.LastUpdate = (DateTime)dateUpdate;
                        }

                        Common.LogDebug(true, $"titleList: {Serialization.ToJson(titleList)}");

                        return titleList;
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    return null;
                }

                return null;
            }
            else
            {
                PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                    $"{PluginDatabase.PluginName}-Import-Error",
                    PluginDatabase.PluginName + System.Environment.NewLine + resources.GetString("LOCCommonNotLoggedIn"),
                    NotificationType.Error,
                    () => PluginDatabase.Plugin.OpenSettingsView()
                ));
                return null;
            }
        }


        public bool EditIdExist(string UserGameId)
        {
            return GetUserGamesListAsync().Id.Equals(UserGameId);
        }

        public string FindIdExisting(string GameId)
        {
            try
            {
                //string UserGamesList = GetUserGamesList();
                string UserGamesList = "";
                HtmlParser parser = new HtmlParser();
                IHtmlDocument htmlDocument = parser.Parse(UserGamesList);

                IElement element = htmlDocument.QuerySelectorAll("a").Where(x => x.GetAttribute("href").Contains($"game?id={GameId}")).FirstOrDefault();

                if (element != null)
                {
                    return element.GetAttribute("id").ToLower().Replace("user_play_sel_", string.Empty);
                }
                else
                {
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
                return null;
            }
        }
        #endregion


        /// <summary>
        /// Post current data in HowLongToBeat website.
        /// </summary>
        /// <param name="hltbPostData"></param>
        /// <returns></returns>
        public async Task<bool> PostData(Game game, HltbPostData hltbPostData)
        {
            if (GetIsUserLoggedIn() && hltbPostData.user_id != 0 && hltbPostData.game_id != 0)
            {
                try
                {
                    Type type = typeof(HltbPostData);
                    PropertyInfo[] properties = type.GetProperties();
                    Dictionary<string, string> data = new Dictionary<string, string>();


                    // Get existing data
                    if (hltbPostData.edit_id != 0)
                    {
                        logger.Info($"Edit {game.Name} - {hltbPostData.edit_id}");
                        data.Add("edited", "Save Edit");
                    }
                    else
                    {
                        logger.Info($"Submit {game.Name}");
                        data.Add("submitted", "Submit");
                    }


                    foreach (PropertyInfo property in properties)
                    {
                        switch (property.Name)
                        {
                            case "list_p":
                                if (property.GetValue(hltbPostData, null).ToString() != string.Empty)
                                {
                                    data.Add(property.Name, property.GetValue(hltbPostData, null).ToString());
                                }
                                break;
                            case "list_b":
                                if (property.GetValue(hltbPostData, null).ToString() != string.Empty)
                                {
                                    data.Add(property.Name, property.GetValue(hltbPostData, null).ToString());
                                }
                                break;
                            case "list_r":
                                if (property.GetValue(hltbPostData, null).ToString() != string.Empty)
                                {
                                    data.Add(property.Name, property.GetValue(hltbPostData, null).ToString());
                                }
                                break;
                            case "list_c":
                                if (property.GetValue(hltbPostData, null).ToString() != string.Empty)
                                {
                                    data.Add(property.Name, property.GetValue(hltbPostData, null).ToString());
                                }
                                break;
                            case "list_cp":
                                if (property.GetValue(hltbPostData, null).ToString() != string.Empty)
                                {
                                    data.Add(property.Name, property.GetValue(hltbPostData, null).ToString());
                                }
                                break;
                            case "list_rt":
                                if (property.GetValue(hltbPostData, null).ToString() != string.Empty)
                                {
                                    data.Add(property.Name, property.GetValue(hltbPostData, null).ToString());
                                }
                                break;


                            case "compmonth":
                                if (property.GetValue(hltbPostData, null).ToString() == string.Empty)
                                {
                                    data.Add(property.Name, DateTime.Now.ToString("MM"));
                                }
                                else
                                {
                                    data.Add(property.Name, property.GetValue(hltbPostData, null).ToString());
                                }
                                break;
                            case "compday":
                                if (property.GetValue(hltbPostData, null).ToString() == string.Empty)
                                {
                                    data.Add(property.Name, DateTime.Now.ToString("dd"));
                                }
                                else
                                {
                                    data.Add(property.Name, property.GetValue(hltbPostData, null).ToString());
                                }
                                break;
                            case "compyear":
                                if (property.GetValue(hltbPostData, null).ToString() == string.Empty)
                                {
                                    data.Add(property.Name, DateTime.Now.ToString("yyyy"));
                                }
                                else
                                {
                                    data.Add(property.Name, property.GetValue(hltbPostData, null).ToString());
                                }
                                break;

                            default:
                                data.Add(property.Name, property.GetValue(hltbPostData, null).ToString());
                                break;
                        }
                    }
                    

                    List<Playnite.SDK.HttpCookie> Cookies = WebViewOffscreen.GetCookies();
                    Cookies = Cookies.Where(x => x != null && x.Domain != null && x.Domain.Contains("howlongtobeat", StringComparison.InvariantCultureIgnoreCase)).ToList();

                    FormUrlEncodedContent formContent = new FormUrlEncodedContent(data);
                    string response = await Web.PostStringDataCookies(UrlPostData, formContent, Cookies);


                    // Check errors
                    HtmlParser parser = new HtmlParser();
                    IHtmlDocument htmlDocument = parser.Parse(response);

                    string errorMessage = string.Empty;
                    foreach (IElement el in htmlDocument.QuerySelectorAll("div.in.back_red.shadow_box li"))
                    {
                        if (errorMessage.IsNullOrEmpty())
                        {
                            errorMessage += el.InnerHtml;
                        }
                        else
                        {
                            errorMessage += System.Environment.NewLine + el.InnerHtml;
                        }
                    }


                    if (!errorMessage.IsNullOrEmpty())
                    {
                        PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                            $"{PluginDatabase.PluginName}-{game.Id}-Error",
                            PluginDatabase.PluginName + System.Environment.NewLine + game.Name + System.Environment.NewLine + errorMessage,
                            NotificationType.Error
                        ));
                    }
                    else
                    {
                        PluginDatabase.RefreshUserData(hltbPostData.game_id);
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    return false;
                }
            }
            else
            {
                PluginDatabase.PlayniteApi.Notifications.Add(new NotificationMessage(
                    $"{PluginDatabase.PluginName}-DataUpdate-Error",
                    PluginDatabase.PluginName + System.Environment.NewLine + resources.GetString("LOCCommonNotLoggedIn"),
                    NotificationType.Error,
                    () => PluginDatabase.Plugin.OpenSettingsView()
                ));
                return false;
            }

            return false;
        }
    }
}
