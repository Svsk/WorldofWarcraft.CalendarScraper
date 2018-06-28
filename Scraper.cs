using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using CalendarScraper.BattleNet;
using CalendarScraper.Configuration;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace CalendarScraper
{
    public class Scraper
    {
        public static By SelectorByAttributeValue(string p_strAttributeName, string p_strAttributeValue)
        {
            return (By.XPath($"//*[@{p_strAttributeName} = '{p_strAttributeValue}']"));
        }

        public void ScrapeCalendar()
        {
            var scrapeConfig = ConfigurationService.Get<ScraperConfig>();

            var options = new ChromeOptions();
            options.AddArguments("headless");
            options.SetLoggingPreference(LogType.Driver, LogLevel.Off);
            var browser = new ChromeDriver(AppContext.BaseDirectory, options);

            var eventIds = ScrapeEventIds(browser);
            var events = ScrapeEvents(browser, eventIds);

            Console.WriteLine($"Scraped {events.Count} events.");

            File.WriteAllText(scrapeConfig.OutputPath, JsonConvert.SerializeObject(events, Formatting.Indented));
        }

        private List<string> ScrapeEventIds(ChromeDriver browser)
        {
            browser.Navigate().GoToUrl("https://eu.battle.net/wow/en/vault/character/event");
            Thread.Sleep(2000);

            LogInToBattleNet(browser);

            var eventElements = browser.FindElements(By.CssSelector(".event-summary:not(.system-event)"));
            var eventIds = eventElements.Select(c => c.GetAttribute("data-id")).ToList();

            return eventIds;
        }

        private List<CalendarEvent> ScrapeEvents(ChromeDriver browser, List<string> eventIds)
        {
            var events = new List<CalendarEvent>();
            var textInfo = CultureInfo.CurrentCulture.TextInfo;

            foreach (var eventId in eventIds)
            {
                var url = $"https://eu.battle.net/wow/en/vault/character/event/details?eventId={eventId}";
                browser.Navigate().GoToUrl(url);

                Thread.Sleep(2000);

                var eventName = browser.FindElement(By.CssSelector(".event-header-text .subheader.name")).Text;
                var time = browser.FindElement(By.CssSelector(".event-invitation .time")).Text; //"Raid - Antorus, the Burning Throne\r\nThu (28/06) 20:00"
                var type = time.Split(" - ").First().Trim();
                var instance = time.Split(" - ").Skip(1).Take(1).First().Split("\r\n").First().Trim();
                var dateTime = time.Split("\r\n").Last();

                var description = browser.FindElement(By.CssSelector(".event-invitation .description")).Text;
                var invites = browser.FindElements(By.CssSelector(".invitation-list li")).Select(invite =>
                {
                    var characterLink = invite.FindElement(By.TagName("a"));
                    var characterClassIcon = invite.FindElement(By.CssSelector(".icon-frame img"));
                    var inviteResponse = invite.FindElement(By.CssSelector(".response"));

                    var name = characterLink.Text.Trim();
                    var realm = characterLink.GetAttribute("href").Split('/').Reverse().Skip(2).Take(1).First();
                    var charClass = characterClassIcon.GetAttribute("src").Split('/').Last().Replace(".jpg", string.Empty).Replace("class_", string.Empty).Replace("-", " ");
                    var inviteStatus = inviteResponse.Text.Trim();

                    return new EventAttendee
                    {
                        Name = textInfo.ToTitleCase(name),
                        Realm = textInfo.ToTitleCase(realm),
                        Class = textInfo.ToTitleCase(charClass),
                        Status = textInfo.ToTitleCase(inviteStatus)
                    };
                }).ToList();

                events.Add(new CalendarEvent {
                    Name = eventName,
                    Type = type,
                    Instance = instance,
                    Description = description,
                    Time = dateTime,
                    InvitationList = invites
                });
            }

            return events;
        }

        private void LogInToBattleNet(ChromeDriver browser)
        {
            var config = ConfigurationService.Get<BattleNetConfig>();

            browser.FindElementById("accountName").SendKeys(config.BattleNetAccount);
            browser.FindElementById("password").SendKeys(config.BattleNetPassword);
            browser.FindElementById("submit").Click();

            Thread.Sleep(2000);

            var element = browser.FindElement(SelectorByAttributeValue("formaction", "/login/en/authenticator/choose/authenticator"));
            element.Click();

            Thread.Sleep(500);

            var authenticator = new BattleNetAuthenticator(config.BattleNetAuthenticatorSerial, config.BattleNetAuthenticatorSecret);
            browser.FindElementById("authValue").SendKeys(authenticator.CurrentCode);
            browser.FindElementById("submit").Click();

            Thread.Sleep(5000);
        }
    }
}