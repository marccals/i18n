﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Hosting;

namespace i18n
{
    /// <summary>
    /// A service for retrieving localized text from PO resource files
    /// </summary>
    public class LocalizingService : ILocalizingService
    {
        private static readonly object Sync = new object();

        /// <summary>
        /// Returns the best matching language for this application's resources, based the provided languages
        /// </summary>
        /// <param name="languages">A sorted list of language preferences</param>
        public virtual string GetBestAvailableLanguageFrom(string[] languages)
        {
            string defaultValue = GetDefaultWebLanguageNameInTwoLetterISOFormat();

            if (languages == null)
            {
                return defaultValue;
            }

            foreach (var language in languages.Where(language => !string.IsNullOrWhiteSpace(language)))
            {
                var culture = GetCultureInfoFromLanguage(language);

                if (culture.TwoLetterISOLanguageName.Equals(GetDefaultWebLanguageNameInTwoLetterISOFormat(), StringComparison.OrdinalIgnoreCase))
                {
                    return culture.IetfLanguageTag;
                }

                // en-US
                var result = GetLanguageIfAvailable(culture.IetfLanguageTag);
                if(result != null)
                {
                    return result;
                }

                // Don't process the same culture code again
                if (culture.IetfLanguageTag.Equals(culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // en
                result = GetLanguageIfAvailable(culture.TwoLetterISOLanguageName);
                if (result != null)
                {
                    return result;
                }
            }

            return defaultValue;
        }

        private static string GetLanguageIfAvailable(string culture)
        {
        // Note that there is no need to serialize access to HttpRuntime.Cache when just reading from it.
        //
            culture = culture.ToLowerInvariant();

            Dictionary<string, I18NMessage> messages = (Dictionary<string, I18NMessage>)HttpRuntime.Cache[GetCacheKey(culture)];

            // If messages not yet loaded in for the language
            if (messages == null)
            {
                // Attempt to load messages, and if failed (because PO file doesn't exist)
                if (!LoadMessages(culture))
                {
                    return null;
                }

                // Address messages just loaded.
                messages = (Dictionary<string, I18NMessage>)HttpRuntime.Cache[GetCacheKey(culture)];
            }

            // The language is considered to be available if one or more message strings exist.
            return messages.Count > 0 ? culture : null;
        }

        /// <summary>
        /// Returns localized text for a given default language key, or the default itself,
        /// based on the provided languages and application resources
        /// </summary>
        /// <param name="key">The default language key to search for</param>
        /// <param name="languages">A sorted list of language preferences</param>
        /// <returns></returns>
        public virtual string GetText(string key, string[] languages)
        {
            // Prefer 'en-US', then 'en', before moving to next language choice
            foreach (var language in languages.Where(language => !string.IsNullOrWhiteSpace(language)))
            {
                var culture = GetCultureInfoFromLanguage(language);

                // Save cycles processing beyond the default; just return the original key
                if (culture.TwoLetterISOLanguageName.Equals(GetDefaultWebLanguageNameInTwoLetterISOFormat(), StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }

                // E.g. en-US
                var regional = TryGetTextFor(culture.IetfLanguageTag, key);

                // If we just tried a region-specific lookup and that failed...try a region-neutral lookup. E.g. fr-CH -> fr.
                if(!culture.IetfLanguageTag.Equals(culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase) && regional == key)
                {
                    var global = TryGetTextFor(culture.TwoLetterISOLanguageName, key);
                    if(global != key)
                    {
                        return global;
                    }
                    continue;
                }

                if(regional != key)
                {
                    return regional;
                }
            }

            return key;
        }

        private static string TryGetTextFor(string culture, string key)
        {
            return GetLanguageIfAvailable(culture) != null ? GetTextOrDefault(culture, key) : key;
        }

        private static bool LoadMessages(string culture)
        {
            string directory;
            string path;
            GetDirectoryAndPath(culture, out directory, out path);

            if (!File.Exists(path))
            {
                return false;
            }

            LoadFromDiskAndCache(culture, path);
            return true;
        }

        private static void GetDirectoryAndPath(string culture, out string directory, out string path)
        {
            directory = string.Format("{0}/locale/{1}", HostingEnvironment.ApplicationPhysicalPath, culture);
            path = Path.Combine(directory, "messages.po");
        }

        private static void LoadFromDiskAndCache(string culture, string path)
        {
            lock (Sync)
            {
                // It is possible for multiple threads to race to this method. The first to
                // enter the above lock will insert the messages into the cache.
                // If we lost the race...no need to duplicate the work of the winning thread.
                if (HttpRuntime.Cache[GetCacheKey(culture)] != null) {
                    return; }

                using (var fs = File.OpenText(path))
                {
                    // http://www.gnu.org/s/hello/manual/gettext/PO-Files.html

                    var messages = new Dictionary<string, I18NMessage>(0);
                    string line;
                    while ((line = fs.ReadLine()) != null)
                    {
                        if (line.StartsWith("#~"))
                        {
                            continue;
                        }

                        var message = new I18NMessage();
                        var sb = new StringBuilder();

                        if (line.StartsWith("#"))
                        {
                            sb.Append(CleanCommentLine(line));
                            while((line = fs.ReadLine()) != null && line.StartsWith("#"))
                            {
                                sb.Append(CleanCommentLine(line));
                            }
                            message.Comment = sb.ToString();

                            sb.Clear();
                            ParseBody(fs, line, sb, message);

                            // Only if a msgstr (translation) is provided for this entry do we add an entry to the cache.
                            // This conditions facilitates more useful operation of the GetLanguageIfAvailable method,
                            // which prior to this condition was indicating a language was available when in fact there
                            // were zero translation in the PO file (it having been autogenerated during gettext merge).
                            if (!string.IsNullOrWhiteSpace(message.MsgStr))
                            {
                                if (!messages.ContainsKey(message.MsgId))
                                {
                                    messages.Add(message.MsgId, message);
                                }
                            }
                        }
                        else if (line.StartsWith("msgid"))
                        {
                            ParseBody(fs, line, sb, message);
                        }
                    }

                    //lock (Sync)
                    {
                        // If the file changes we want to be able to rebuild the index without recompiling
                        HttpRuntime.Cache.Insert(GetCacheKey(culture), messages, new CacheDependency(path));
                    }
                }
            }
        }

        private static void ParseBody(TextReader fs, string line, StringBuilder sb, I18NMessage message)
        {
            if(!string.IsNullOrEmpty(line))
            {
                if(line.StartsWith("msgid"))
                {
                    var msgid = line.Unquote();
                    sb.Append(msgid);

                    while ((line = fs.ReadLine()) != null && !line.StartsWith("msgstr") && (msgid = line.Unquote()) != null)
                    {
                        sb.Append(msgid);
                    }

                    message.MsgId = sb.ToString().Unescape();
                }

                sb.Clear();
                if(!string.IsNullOrEmpty(line) && line.StartsWith("msgstr"))
                {
                    var msgstr = line.Unquote();
                    sb.Append(msgstr);

                    while ((line = fs.ReadLine()) != null && (msgstr = line.Unquote()) != null)
                    {
                        sb.Append(msgstr);
                    }

                    message.MsgStr = sb.ToString().Unescape();
                }
            }
        }

        private static string CleanCommentLine(string line)
        {
            return line.Replace("# ", "").Replace("#. ", "").Replace("#: ", "").Replace("#, ", "").Replace("#| ", "");
        }

        private static string GetTextOrDefault(string culture, string key)
        {
        // Note that there is no need to serialize access to HttpRuntime.Cache when just reading from it.
        //
            var messages = (Dictionary<string, I18NMessage>) HttpRuntime.Cache[GetCacheKey(culture)];
            I18NMessage message = null;

            if (messages == null || !messages.TryGetValue(key, out message))
            {
                return key;
            }

            return message.MsgStr;
                // We check this for null/empty before adding to collection.
        }

        private static CultureInfo GetCultureInfoFromLanguage(string language)
        {
            //var semiColonIndex = language.IndexOf(';');
            //return semiColonIndex > -1
            //           ? new CultureInfo(language.Substring(0, semiColonIndex), true)
            //           : new CultureInfo(language, true);
            //Codes wouldn't work on ie10 of some languages of Windows 8 and Windows 2012

            var semiColonIndex = language.IndexOf(';');
            language = semiColonIndex > -1 ? language.Substring(0, semiColonIndex) : language;
            language = System.Globalization.CultureInfo.CreateSpecificCulture(language).Name;
            return new CultureInfo(language, true);
        }

        private static string GetCacheKey(string culture)
        {
            return string.Format("po:{0}", culture).ToLowerInvariant();
        }

        /// <summary>
        /// Gets the Default Language of Web
        /// </summary>
        private static string GetDefaultWebLanguageNameInTwoLetterISOFormat()
        {
            return I18NSession.GetDefaultLanguageWebFromSession(HttpContext.Current);
        }
    }
}
