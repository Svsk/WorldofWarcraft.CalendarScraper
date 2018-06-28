namespace CalendarScraper
{
    class Program
    {
        static void Main(string[] args)
        {
            var scraper = new Scraper();
            scraper.ScrapeCalendar();
        }
    }
}
