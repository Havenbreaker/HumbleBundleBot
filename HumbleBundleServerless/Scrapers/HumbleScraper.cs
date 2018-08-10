﻿using System;
using HtmlAgilityPack;
using HumbleBundleServerless.Models;
using Newtonsoft.Json;
using ScrapySharp.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HumbleBundleBot
{
    public class HumbleScraper
    {

        private readonly List<string> _visitedUrls = new List<string>();

        private readonly List<HumbleBundle> _foundBundles = new List<HumbleBundle>();

        public string BaseUrl { get; set; }

        private const string HumbleBundleUrl = "https://www.humblebundle.com/";

        private List<dynamic> BundlesTab = new List<dynamic>();

        public HumbleScraper(string baseUrl)
        {
            BaseUrl = baseUrl;
        }

        public List<HumbleBundle> Scrape()
        {
            ScrapePage(BaseUrl);

            return _foundBundles;
        }

        private void ScrapePage(string url)
        {
            var web = new HtmlWeb();
            var document = web.Load(url);
            var response = document.DocumentNode;

            var finalUrl = GetOgPropertyValue(response, "url");

            if (!BundlesTab.Any())
            {
                BundlesTab = GetBundlesTab(response).ToList();
            }

            _visitedUrls.Add(url);
            _visitedUrls.Add(finalUrl);

            if (url == BaseUrl)
            {
                VisitOtherPages(BundlesTab);
            }
            else
            {
                try
                {
                    var bundle = new HumbleBundle
                    {
                        Name = GetOgPropertyValue(response, "title"),
                        Description = GetOgPropertyValue(response, "description"),
                        ImageUrl = GetOgPropertyValue(response, "image"),
                        URL = finalUrl,
                        Type = GetBundleType(url)
                    };

                    ScrapeSections(bundle, response);

                    _foundBundles.Add(bundle);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                    // Do nothing
                }
            }

        }

        private static string GetOgPropertyValue(HtmlNode response, string property)
        {
            return response.CssSelect("meta").Where(x => x.Attributes.HasKeyIgnoreCase("property")).First(x => x.Attributes["property"].Value == "og:" + property).Attributes["content"].Value;
        }

        /**
         * The bundles tab is injected via JS after page load. The data it injects is already in-page, however, so we can
         * parse and deserialize it to get the data we want.
         **/
        private static IEnumerable<dynamic> GetBundlesTab(HtmlNode response)
        {
            const string startString = "\"mosaic\":";
            const string endString = "\"user\": {}";

            var jsonResponse = response.InnerHtml.Substring(response.InnerHtml.IndexOf(startString, StringComparison.Ordinal) + startString.Length);

            var endIndex = jsonResponse.IndexOf(endString, StringComparison.Ordinal);
            jsonResponse = jsonResponse.Substring(0, endIndex - "\r\n      ".Length);

            jsonResponse = jsonResponse.Replace("True", "true").Replace("False", "false");

            var settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            var converted = JsonConvert.DeserializeObject<List<dynamic>>(jsonResponse, settings);

            return converted[0]["products"];
        }

        private static BundleTypes GetBundleType(string url)
        {
            if (url.Contains("games") || url.Contains("monthly"))
            {
                return BundleTypes.GAMES;
            }
            if (url.Contains("mobile"))
            {
                return BundleTypes.MOBILE;
            }
            if (url.Contains("books")) || (url.Contains("comics"))
            {
                return BundleTypes.BOOKS;
            }
            if (url.Contains("software"))
            {
                return BundleTypes.SOFTWARE;
            }
            return BundleTypes.SPECIAL;
        }

        private static void ScrapeSections(HumbleBundle bundle, HtmlNode response)
        {

            foreach (var parsedSection in response.CssSelect(".dd-game-row"))
            {
                string sectionTitle;

                try
                {
                    try
                    {
                        sectionTitle = parsedSection.CssSelect(".dd-header-headline").First().InnerText
                            .CleanInnerText();
                    }
                    catch
                    {
                        sectionTitle = parsedSection.CssSelect(".fi-content-header").First().InnerText.CleanInnerText();
                    }
                }
                catch (Exception)
                {
                    sectionTitle = string.Empty;
                }

                if (sectionTitle.Contains("average"))
                {
                    sectionTitle = "Beat the Average!";
                }

                if (string.IsNullOrEmpty(sectionTitle))
                {
                    continue;
                }

                var sectionToAdd = new HumbleSection()
                {
                    Title = sectionTitle
                };

                FindGamesInSection(parsedSection, sectionToAdd);

                bundle.Sections.Add(sectionToAdd);
            }
        }

        private static void FindGamesInSection(HtmlNode parsedSection, HumbleSection section)
        {
            foreach (var itemTitle in parsedSection.CssSelect(".dd-image-box-caption"))
            {
                var itemName = itemTitle.InnerText.CleanInnerText();
                if (section.Items.All(x => x.Name != itemName) && !itemName.StartsWith("More in"))
                {
                    section.Items.Add(new HumbleItem()
                    {
                        Name = itemName
                    });
                }
            }

            if (parsedSection.CssSelect(".fi-content-body").Any())
            {
                var itemName = parsedSection.CssSelect(".fi-content-body").First().InnerText.CleanInnerText();
                if (section.Items.All(x => x.Name != itemName) && !itemName.StartsWith("More in"))
                {
                    section.Items.Add(new HumbleItem()
                    {
                        Name = itemName
                    });
                }
            }
        }

        private void VisitOtherPages(IEnumerable<dynamic> bundlesTab)
        {
            foreach (var tab in bundlesTab)
            {
                string nextPage = HumbleBundleUrl.Replace(".com/", ".com") + tab.product_url;

                if (!_visitedUrls.Contains(nextPage) && !nextPage.Contains("store"))
                {
                    ScrapePage(nextPage);
                }
            }
        }
    }
}
