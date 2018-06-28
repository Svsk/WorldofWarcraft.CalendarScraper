using System;
using Microsoft.Extensions.Configuration;

namespace CalendarScraper.Configuration
{
    //Put an interface on this calss if you want to test it.
    public class ConfigurationService
    {
        public static T Get<T>() where T : new()
        {
            //Set up configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("config.json", optional: false, reloadOnChange: false)
                .AddJsonFile("config.Local.json", optional: true, reloadOnChange: false);

            var config = builder.Build();
            var configClass = new T();

            config.GetSection(typeof(T).Name).Bind(configClass);

            return configClass;
        }
    }
}