using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TwitterTweetTracker
{
    class Program
    {
        static void Main(string[] args)
        {
            TwitterTweetTracker.TweetTracker tracker = new TweetTracker();

            tracker.Track();
        }
    }
}
