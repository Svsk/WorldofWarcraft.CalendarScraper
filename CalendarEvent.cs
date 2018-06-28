using System.Collections.Generic;

namespace CalendarScraper
{
    public class CalendarEvent
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Instance { get; set; }
        public string Description { get; set; }
        public string Time { get; set; }
        public IEnumerable<EventAttendee> InvitationList { get; set; }
    }
}