using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shaman.Runtime;
#if !STANDALONE
using HttpUtils = Shaman.Utils;
#endif
namespace Shaman
{
    /// <summary>
    /// Represents the credentials for a website.
    /// </summary>
    public class Credentials
    {
        internal Credentials()
        {
        }


        internal long Id { get; set; }

        /// <summary>
        /// Gets the site identifier.
        /// </summary>
        public string SiteIdentifier { get; internal set; }

        /// <summary>
        /// Gets or sets the user name (or email address where applicable).
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Gets or sets the password.
        /// </summary>
        public string Password { get; set; }
        internal string LastCookies { get; set; }

#if !STANDALONE
        public void Save()
        {
            ObjectManager.SaveCredentials(this);
        }
#endif

        public void ClearCookies()
        {
            LastCookies = null;
        }

        public string GetCookie(string name)
        {
            if (LastCookies == null) return null;
            return HttpUtils.GetParameters(LastCookies).FirstOrDefault(x => x.Key == name).Value;
        }

        /// <summary>
        /// Gets the date of the last login.
        /// </summary>
        public DateTime? LastLoginDate { get; internal set; }

        internal string GetSessionCookie()
        {
            return string.Join(";",
             HttpUtils.GetParameters(LastCookies).Where(x => HttpUtils.Configuration_SessionCookieNames.Contains(x.Key.ToLowerFast())).Select(x => x.Value)
             #if NET35
             .ToArray()
             #endif
             );
        }
    }
}
