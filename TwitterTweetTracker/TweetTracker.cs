using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.Data;
using TweetinCore.Interfaces;
using System.Collections.Specialized;
using TwitterUserTimeLine;
using Tweetinvi;
using TwitterToken;

///
/// List of User ID's to track
/// Tweets in this Timeframe - hours, days, etc
/// How long to track, in days
/// Do a cycle every X hours
///

namespace TwitterTweetTracker
{
    class TweetTracker
    {
        #region Members
        string _ConsumerKey;
        string _ConsumerSecret;
        string _AccessToken;
        string _AccessTokenSecret;
        private readonly string _DBNamePath;
        private readonly string _DBName;
        private readonly string _SqlServerName;
        internal readonly string _ConnStringInitial;
        internal readonly string _ConnString;
        public string _TweetsTableSchema;
        internal readonly string[] _TweetsTableColumns;
        public string _ReTweetsTableSchema;
        internal readonly string[] _RetweetsTableColumns;
        public string _UsersTableSchema;
        internal readonly string[] _UsersTableColumns;
        private readonly string[] _TableNames;
        private readonly string _SqlCommandCreateDB;
        private readonly string[] _TableSchemas;
        private TimeSpan _TimeFrame;
        private TimeSpan _TrackingPeriod;
        private TimeSpan _TimeBetweenCycles;
        private List<KeyValuePair<string, string>> _Users = new List<KeyValuePair<string, string>>();

        private TwitterUserTimeLine.TwitterAPI _TwitterAPI;
        private IToken _Token;
        #endregion

        public TweetTracker()
        {
            //Read settings from app.config
            _ConsumerKey = System.Configuration.ConfigurationManager.AppSettings["ConsumerKey"];
            _ConsumerSecret = System.Configuration.ConfigurationManager.AppSettings["ConsumerSecret"];
            _AccessToken = System.Configuration.ConfigurationManager.AppSettings["AccessToken"];
            _AccessTokenSecret = System.Configuration.ConfigurationManager.AppSettings["AccessTokenSecret"];
            _DBNamePath = System.Configuration.ConfigurationManager.AppSettings["DBNamePath"];
            _DBName = System.Configuration.ConfigurationManager.AppSettings["DBName"];
            _SqlServerName = System.Configuration.ConfigurationManager.AppSettings["SQLServerName"];
            _TimeFrame = TimeSpan.Parse(System.Configuration.ConfigurationManager.AppSettings["Timeframe"]);
            _TrackingPeriod = TimeSpan.Parse(System.Configuration.ConfigurationManager.AppSettings["TrackingPeriod"]);
            int timeBetweenCycles = int.Parse(System.Configuration.ConfigurationManager.AppSettings["TimeBetweenCycles"]);
            _TimeBetweenCycles = new TimeSpan(timeBetweenCycles, 0, 0);
            ReadConfigurationSection("UsersToTrack", ref _Users);
            _Token = new Token(_AccessToken, _AccessTokenSecret, _ConsumerKey, _ConsumerSecret);

            //Init basic variables
            _ConnStringInitial = "Server=" + Environment.MachineName + "\\SQLEXPRESS;User Id=sa;Password=tokash30;database=master";
            _ConnString = "Server=" + Environment.MachineName + "\\SQLEXPRESS;User Id=sa;Password=tokash30;database=" + _DBName;
            _TweetsTableSchema = @"(TweetID nvarchar (25) PRIMARY KEY, UserID nvarchar (25), Tweet nvarchar (4000), TimeOfTweet nvarchar (40))";
            _TweetsTableColumns = new string[]{ "TweetID", "UserID", "Tweet", "TimeOfTweet"};
            _ReTweetsTableSchema = @"(ReTweetID nvarchar (25) PRIMARY KEY, UserID nvarchar (25), SourceTweetID nvarchar (25), SourceUserID nvarchar (25), TimeOfReTweet nvarchar (40))";
            _RetweetsTableColumns = new string[] { "ReTweetID", "UserID", "SourceTweetID", "SourceUserID", "TimeOfReTweet" };
            _UsersTableSchema = @"(UserID nvarchar (25), Name nvarchar (30) not null))";
            _UsersTableColumns = new string[]{ "UserID", "Name" };

            _TableNames = new string[] { "Tweets", "Retweets", "Users"};
            _TableSchemas = new string[] { "CREATE TABLE Tweets (TweetID nvarchar (25) PRIMARY KEY, UserID nvarchar (25), Tweet nvarchar (4000), TimeOfTweet nvarchar (40))",
                                           "CREATE TABLE Retweets (ReTweetID nvarchar (25) PRIMARY KEY, UserID nvarchar (25), SourceTweetID nvarchar (25), SourceUserID nvarchar (25), TimeOfReTweet nvarchar (40))",
                                           "CREATE TABLE Users (UserID nvarchar (25) PRIMARY KEY, Name nvarchar (30) not null)"};

            _SqlCommandCreateDB = "CREATE DATABASE " + _DBName + " ON PRIMARY " +
                "(NAME = " + _DBName + ", " +
                "FILENAME = '" + _DBNamePath + _DBName + ".mdf', " +
                "SIZE = 3MB, MAXSIZE = 10MB, FILEGROWTH = 10%) " +
                "LOG ON (NAME = " + _DBName + "_LOG, " +
                "FILENAME = '" + _DBNamePath + _DBName + ".ldf', " +
                "SIZE = 1MB, " +
                "MAXSIZE = 100MB, " +
                "FILEGROWTH = 10%)";

            _TwitterAPI = new TwitterAPI(_ConsumerKey, _ConsumerSecret);

            CreateEmptyDB();
        }

        private void AddTweetToDB(ITweet iTweet)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            parameters.Add(String.Format("@{0}", _TweetsTableColumns[0]), iTweet.IdStr);
            parameters.Add(String.Format("@{0}", _TweetsTableColumns[1]), iTweet.Creator.IdStr);
            parameters.Add(String.Format("@{0}", _TweetsTableColumns[2]), iTweet.Text.Replace("\n", ""));
            parameters.Add(String.Format("@{0}", _TweetsTableColumns[3]), iTweet.CreatedAt.ToString());

            try
            {
                string sqlCmd = string.Format("select TweetID from tweets where TweetID='{0}'", iTweet.IdStr);
                DataTable result = SQLServerCommon.SQLServerCommon.ExecuteQuery(sqlCmd, _ConnString);
                if (result.Rows.Count == 0)
                {
                    SQLServerCommon.SQLServerCommon.Insert("Tweets", _ConnString, _TweetsTableColumns, parameters);
                }
            }
            catch (Exception)
            {

                throw;
            }

            //if (iTweet.Hashtags != null)
            //{
            //    foreach (IHashTagEntity hashtag in iTweet.Hashtags)
            //    {
            //        AddHashTagToDB(iTweet.IdStr, hashtag.Text);
            //    }
            //}

            if (iTweet.Retweets != null)
            {
                foreach (ITweet retweet in iTweet.Retweets)
                {
                    AddReTweetToDB(retweet, iTweet);
                }
            }
        }

        private void AddReTweetToDB(ITweet iReTweet, ITweet iTweet)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            parameters.Add(String.Format("@{0}", _RetweetsTableColumns[0]), iReTweet.IdStr);
            parameters.Add(String.Format("@{0}", _RetweetsTableColumns[1]), iReTweet.Creator.IdStr);
            parameters.Add(String.Format("@{0}", _RetweetsTableColumns[2]), iTweet.IdStr);
            parameters.Add(String.Format("@{0}", _RetweetsTableColumns[3]), iTweet.Creator.IdStr);
            parameters.Add(String.Format("@{0}", _RetweetsTableColumns[4]), iReTweet.CreatedAt.ToString());


            try
            {
                string sqlCmd = string.Format("select ReTweetID from Retweets where ReTweetID='{0}'", iReTweet.IdStr);
                DataTable result = SQLServerCommon.SQLServerCommon.ExecuteQuery(sqlCmd, _ConnString);
                if (result.Rows.Count == 0)
                {
                    SQLServerCommon.SQLServerCommon.Insert("Retweets", _ConnString, _RetweetsTableColumns, parameters);
                }
            }
            catch (Exception)
            {

                throw;
            }
        }

        private void AddReTweetToDB(ITweet iReTweet, string iSourceTweetID, string iSourceUserID)
        {            
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            parameters.Add(String.Format("@{0}", _RetweetsTableColumns[0]), iReTweet.IdStr);
            parameters.Add(String.Format("@{0}", _RetweetsTableColumns[1]), iReTweet.Creator.IdStr);
            parameters.Add(String.Format("@{0}", _RetweetsTableColumns[2]), iSourceTweetID);
            parameters.Add(String.Format("@{0}", _RetweetsTableColumns[3]), iSourceUserID);
            parameters.Add(String.Format("@{0}", _RetweetsTableColumns[4]), iReTweet.CreatedAt.ToString());


            try
            {
                string sqlCmd = string.Format("select ReTweetID from Retweets where ReTweetID='{0}'", iReTweet.IdStr);
                DataTable result = SQLServerCommon.SQLServerCommon.ExecuteQuery(sqlCmd, _ConnString);
                if (result.Rows.Count == 0)
                {
                    SQLServerCommon.SQLServerCommon.Insert("Retweets", _ConnString, _RetweetsTableColumns, parameters);
                }
            }
            catch (Exception)
            {

                throw;
            }
        }

        public void AddUserToDB(string iUserID, string iUsername)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            parameters.Add(String.Format("@{0}", _UsersTableColumns[0]), iUserID);
            parameters.Add(String.Format("@{0}", _UsersTableColumns[1]), iUsername);


            try
            {
                string sqlCmd = string.Format("select UserID from Users where UserID='{0}'", iUserID);
                DataTable result = SQLServerCommon.SQLServerCommon.ExecuteQuery(sqlCmd, _ConnString);
                if (result.Rows.Count == 0)
                {
                    SQLServerCommon.SQLServerCommon.Insert("Users", _ConnString, _UsersTableColumns, parameters);
                }
            }
            catch (Exception)
            {

                throw;
            }
        }

        private void CreateEmptyDB()
        {
            int i = 0;
            try
            {
                //Create DB

                if (!SQLServerCommon.SQLServerCommon.IsDatabaseExists(_ConnStringInitial, _DBName))//connStringInitial, dbName))
                {
                    Console.WriteLine(string.Format("Creating DB: {0}", _DBName));
                    SQLServerCommon.SQLServerCommon.ExecuteNonQuery(_SqlCommandCreateDB, _ConnStringInitial);
                    Console.WriteLine(string.Format("Creating DB: {0} - Succeeded.", _DBName));

                    foreach (string tableName in _TableNames)
                    {
                        Console.WriteLine(string.Format("Creating Table: {0}", tableName));
                        Console.WriteLine(string.Format("With the following schema: {0}", _TableSchemas[i]));
                        SQLServerCommon.SQLServerCommon.ExecuteNonQuery(_TableSchemas[i], _ConnString);
                        Console.WriteLine(string.Format("Creating Table: {0} - Succeeded.", tableName));

                        i++;
                    }

                }
                else
                {
                    //Check if all tables exist, if not, create them

                    foreach (string tableName in _TableNames)
                    {
                        if (SQLServerCommon.SQLServerCommon.IsTableExists(_ConnString, _DBName, tableName) == false)
                        {
                            Console.WriteLine(string.Format("Creating Table: {0}", tableName));
                            Console.WriteLine(string.Format("With the following schema: {0}", _TableSchemas[i]));
                            SQLServerCommon.SQLServerCommon.ExecuteNonQuery(_TableSchemas[i], _ConnString);
                            Console.WriteLine(string.Format("Creating Table: {0} - Succeeded.", tableName));
                        }
                        i++;
                    }
                }

            }
            catch (Exception)
            {
                throw;
            }
        }

        private void ReadConfigurationSection(string iConfigurationSection, ref List<KeyValuePair<string, string>> oContainer)
        {
            NameValueCollection temp = (NameValueCollection)ConfigurationManager.GetSection(iConfigurationSection);

            foreach (string key in temp)
            {
                oContainer.Add(new KeyValuePair<string, string>(key, temp[key]));
            }
        }

        public void Track()
        {
            DateTime currentTime = DateTime.Now;


            //Get new tweets to track from tweeter
            do
            {
                foreach (KeyValuePair<string, string> TwitterUserID in _Users)
                {
                    AddUserToDB(TwitterUserID.Value, TwitterUserID.Key);
                    Console.WriteLine(string.Format("{0}: Getting tweets for: {1}", DateTime.Now, TwitterUserID.Key));

                    //Get User's tweets
                    List<TweetObject> userTweets = _TwitterAPI.GetUserTimeLineInTimeFrame(TwitterUserID.Value, _TimeFrame);

                    if (userTweets != null)
                    {
                        try
                        {
                            List<ITweet> Tweets = ConvertListTweetObjectToTweet(userTweets);
                            foreach (ITweet tweet in Tweets)
                            {
                                //Checking to see if the tweet is a retweet - I only consider tweets a valid
                                if (!tweet.Text.Contains("RT"))
                                {
                                    //Get retweets for current tweet
                                    List<TweetObject> tweetRetweets = _TwitterAPI.GetRetweetsForSpecificTweet(tweet.IdStr);
                                    List<ITweet> retweets = null;
                                    if (tweetRetweets != null)
                                    {
                                        retweets = ConvertListTweetObjectToTweet(tweetRetweets);
                                    }

                                    tweet.Retweets = retweets;
                                    AddTweetToDB(tweet); 
                                }
                            }

                        }
                        catch (Exception)
                        {
                            
                            throw;
                        }
                    }

                    Console.WriteLine(string.Format("{0}: Done getting tweets for: {1}", DateTime.Now, TwitterUserID.Key));
                }

                //Go over older tweets to see if new retweets were done
                //read from DB, go over tweets that havent yet passed tracking time
                //get the tweet from db, compare tweet created_date with tracking period
                Console.WriteLine(string.Format("{0}: Tracking retweets of older tweets...", DateTime.Now));

                List<string> trackedTweets = GetTrackedTweetsIDsFromDB();
                foreach (string sourceTweetID in trackedTweets)
                {
                    Console.WriteLine(string.Format("{0}: Tracking retweets for: {1}", DateTime.Now, sourceTweetID));

                    //Get retweets for current tweet
                    List<TweetObject> tweetRetweets = _TwitterAPI.GetRetweetsForSpecificTweet(sourceTweetID);
                    List<ITweet> retweets = null;
                    if (tweetRetweets != null)
                    {
                        retweets = ConvertListTweetObjectToTweet(tweetRetweets);
                        string sourceUserID = GetUserIDFromTweet(sourceTweetID);

                        if (sourceUserID != string.Empty)
                        {
                            foreach (ITweet retweet in retweets)
                            {
                                AddReTweetToDB(retweet, sourceTweetID, sourceUserID);
                            } 
                        }
                    }

                    Console.WriteLine(string.Format("{0}: Done tracking retweets for: {1}", DateTime.Now, sourceTweetID));
                }

                Console.WriteLine(string.Format("{0}: Done tracking retweets for older tweets...", DateTime.Now));

                TimeSpan timeGap = currentTime + _TimeBetweenCycles - DateTime.Now;
                if (timeGap < _TimeBetweenCycles && timeGap > new TimeSpan(0,0,0,0))
                {
                    Console.WriteLine(string.Format("{0}: Sleeping until next cycle (sleeping for {1})", DateTime.Now, timeGap.ToString()));
                    System.Threading.Thread.Sleep((int)timeGap.TotalSeconds * 1000);
                    Console.WriteLine(string.Format("{0}: Next cycle begins now", DateTime.Now));
                }

                currentTime = DateTime.Now;

            } while (true);
        }

        private ITweet ConvertTweetObjectToTweet(TweetObject iTweet)
        {
            ITweet tweet;

            try
            {
                Dictionary<string, object> dTweet = new Dictionary<string, object>();

                dTweet.Add("contributors", iTweet.contributors);
                dTweet.Add("coordinates", iTweet.coordinates);
                dTweet.Add("created_at", iTweet.created_at);
                dTweet.Add("entities", iTweet.entities);
                dTweet.Add("favorite_count", iTweet.favorite_count);
                dTweet.Add("favorited", iTweet.favorited);
                dTweet.Add("geo", iTweet.geo);
                dTweet.Add("id", iTweet.id);
                dTweet.Add("id_str", iTweet.id_str);
                dTweet.Add("in_reply_to_screen_name", iTweet.in_reply_to_screen_name);
                dTweet.Add("in_reply_to_status_id", iTweet.in_reply_to_status_id);
                dTweet.Add("in_reply_to_status_id_str", iTweet.in_reply_to_status_id_str);
                dTweet.Add("in_reply_to_user_id", iTweet.in_reply_to_user_id);
                dTweet.Add("in_reply_to_user_id_str", iTweet.in_reply_to_user_id_str);
                dTweet.Add("lang", iTweet.lang);
                dTweet.Add("place", iTweet.place);
                dTweet.Add("possibly_sensitive", iTweet.possibly_sensitive);
                dTweet.Add("retweet_count", iTweet.retweet_count);
                dTweet.Add("retweeted", iTweet.retweeted);
                dTweet.Add("retweeted_status", iTweet.retweeted_status);
                dTweet.Add("source", iTweet.source);
                dTweet.Add("text", iTweet.text);
                dTweet.Add("truncated", iTweet.truncated);
                dTweet.Add("user", iTweet.user);

                tweet = new Tweet(dTweet);
            }
            catch (Exception)
            {

                throw;
            }

            return tweet;
        }

        private List<ITweet> ConvertListTweetObjectToTweet(List<TweetObject> iTweets)
        {
            List<ITweet> tweetObjects = new List<ITweet>();

            foreach (TweetObject tweet in iTweets)
            {
                ITweet t = ConvertTweetObjectToTweet(tweet);

                if (t != null)
                {
                    tweetObjects.Add(t);
                }
            }

            return tweetObjects;
        }

        private List<string> GetTrackedTweetsIDsFromDB()
        {
            List<string> tweetIDs = new List<string>();

            try
            {
                string sqlCmd = string.Format("select * from tweets");
                DataTable result = SQLServerCommon.SQLServerCommon.ExecuteQuery(sqlCmd, _ConnString);
                if (result.Rows.Count != 0)
                {
                    //Convert raw data to tweets
                    foreach (DataRow row in result.Rows)
                    {
                        Dictionary<string, object> dict = new Dictionary<string,object>();
                        dict.Add("id", row["TweetID"]);
                        dict.Add("created_at", row["TimeOfTweet"]);

                        DateTime createdAt = DateTime.Parse((string)dict["created_at"]);


                        if (DateTime.Now - createdAt <= _TrackingPeriod)
                        {
                            tweetIDs.Add((string)dict["id"]);
                        }
                    }
                }
            }
            catch (Exception)
            {

                throw;
            }

            return tweetIDs;
        }

        private string GetUserIDFromTweet(string iTweetID)
        {
            string iuserID = string.Empty;

            try
            {
                string sqlCmd = string.Format("select UserID from tweets where TweetID='{0}'", iTweetID);
                DataTable result = SQLServerCommon.SQLServerCommon.ExecuteQuery(sqlCmd, _ConnString);
                if (result.Rows.Count != 0)
                {
                    iuserID = (string)result.Rows[0]["UserID"];
                }
            }
            catch (Exception)
            {

                throw;
            }

            return iuserID;
        }
    }
}
