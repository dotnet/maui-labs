namespace CometWeather;

public static class WeatherData
{
    public static readonly List<Forecast> Week = new()
    {
        new Forecast { DateTime = DateTime.Today.AddDays(1), Day = new Day { Phrase = "fluent_weather_sunny_high_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 52 }, Maximum = new Maximum { Unit = "F", Value = 77 } } },
        new Forecast { DateTime = DateTime.Today.AddDays(2), Day = new Day { Phrase = "fluent_weather_partly_cloudy" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 61 }, Maximum = new Maximum { Unit = "F", Value = 82 } } },
        new Forecast { DateTime = DateTime.Today.AddDays(3), Day = new Day { Phrase = "fluent_weather_rain_showers_day_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 62 }, Maximum = new Maximum { Unit = "F", Value = 77 } } },
        new Forecast { DateTime = DateTime.Today.AddDays(4), Day = new Day { Phrase = "fluent_weather_thunderstorm_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 57 }, Maximum = new Maximum { Unit = "F", Value = 80 } } },
        new Forecast { DateTime = DateTime.Today.AddDays(5), Day = new Day { Phrase = "fluent_weather_thunderstorm_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 49 }, Maximum = new Maximum { Unit = "F", Value = 61 } } },
        new Forecast { DateTime = DateTime.Today.AddDays(6), Day = new Day { Phrase = "fluent_weather_partly_cloudy" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 49 }, Maximum = new Maximum { Unit = "F", Value = 68 } } },
        new Forecast { DateTime = DateTime.Today.AddDays(7), Day = new Day { Phrase = "fluent_weather_rain_showers_day_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 47 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
    };

    public static readonly List<Forecast> Hours = new()
    {
        new Forecast { DateTime = DateTime.Now.AddHours(1),  Day = new Day { Phrase = "fluent_weather_rain_showers_day_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 47 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(2),  Day = new Day { Phrase = "fluent_weather_rain_showers_day_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 47 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(3),  Day = new Day { Phrase = "fluent_weather_rain_showers_day_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 48 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(4),  Day = new Day { Phrase = "fluent_weather_rain_showers_day_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 49 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(5),  Day = new Day { Phrase = "fluent_weather_cloudy_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 52 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(6),  Day = new Day { Phrase = "fluent_weather_cloudy_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 53 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(7),  Day = new Day { Phrase = "fluent_weather_cloudy_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 58 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(8),  Day = new Day { Phrase = "fluent_weather_sunny_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 63 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(9),  Day = new Day { Phrase = "fluent_weather_sunny_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 64 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(10), Day = new Day { Phrase = "fluent_weather_sunny_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 65 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(11), Day = new Day { Phrase = "fluent_weather_sunny_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 68 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(12), Day = new Day { Phrase = "fluent_weather_sunny_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 68 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(13), Day = new Day { Phrase = "fluent_weather_sunny_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 68 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(14), Day = new Day { Phrase = "fluent_weather_sunny_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 65 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(15), Day = new Day { Phrase = "fluent_weather_sunny_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 63 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(16), Day = new Day { Phrase = "fluent_weather_sunny_20_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 60 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(17), Day = new Day { Phrase = "fluent_weather_moon_16_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 58 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(18), Day = new Day { Phrase = "fluent_weather_moon_16_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 54 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(19), Day = new Day { Phrase = "fluent_weather_moon_16_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 53 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(20), Day = new Day { Phrase = "fluent_weather_moon_16_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 52 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(21), Day = new Day { Phrase = "fluent_weather_moon_16_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 50 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(22), Day = new Day { Phrase = "fluent_weather_moon_16_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 47 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
        new Forecast { DateTime = DateTime.Now.AddHours(23), Day = new Day { Phrase = "fluent_weather_moon_16_filled" }, Temperature = new Temperature { Minimum = new Minimum { Unit = "F", Value = 47 }, Maximum = new Maximum { Unit = "F", Value = 67 } } },
    };

    public static readonly List<Metric> Metrics = new()
    {
        new Metric { Icon = "solid_humidity", Title = "Humidity",        WeatherStation = "Pond Elementary", Value = "78%" },
        new Metric { Icon = "rain_icon",      Title = "Rain",            WeatherStation = "Pond Elementary", Value = "0.2in" },
        new Metric { Icon = "sm_solid_umbrella", Title = "Chance of Rain", WeatherStation = "County Library", Value = "2%" },
        new Metric { Icon = "solid_wind",     Title = "Wind",            WeatherStation = "Pond Elementary", Value = "9mph" },
        new Metric { Icon = "sunrise_icon",   Title = "Sunrise",         WeatherStation = "City Hall",       Value = "6:14am" },
        new Metric { Icon = "sunset_icon",    Title = "Sunset",          WeatherStation = "City Hall",       Value = "8:32pm" },
    };

    public static readonly List<Location> Locations = new()
    {
        new Location { Name = "Redmond",       Icon = "fluent_weather_cloudy_20_filled",            WeatherStation = "USA", Value = "62°" },
        new Location { Name = "St. Louis",     Icon = "fluent_weather_rain_showers_night_20_filled", WeatherStation = "USA", Value = "74°" },
        new Location { Name = "Boston",        Icon = "fluent_weather_cloudy_20_filled",            WeatherStation = "USA", Value = "54°" },
        new Location { Name = "NYC",           Icon = "fluent_weather_cloudy_20_filled",            WeatherStation = "USA", Value = "63°" },
        new Location { Name = "Amsterdam",     Icon = "fluent_weather_cloudy_20_filled",            WeatherStation = "NLD", Value = "49°" },
        new Location { Name = "Seoul",         Icon = "fluent_weather_cloudy_20_filled",            WeatherStation = "KOR", Value = "56°" },
        new Location { Name = "Johannesburg",  Icon = "fluent_weather_sunny_20_filled",             WeatherStation = "ZAF", Value = "62°" },
        new Location { Name = "Rio",           Icon = "fluent_weather_sunny_20_filled",             WeatherStation = "BRA", Value = "79°" },
        new Location { Name = "Madrid",        Icon = "fluent_weather_sunny_20_filled",             WeatherStation = "ESP", Value = "71°" },
        new Location { Name = "Buenos Aires",  Icon = "fluent_weather_sunny_20_filled",             WeatherStation = "ARG", Value = "61°" },
        new Location { Name = "Punta Cana",    Icon = "fluent_weather_rain_showers_day_20_filled",  WeatherStation = "DOM", Value = "84°" },
        new Location { Name = "Hyderabad",     Icon = "fluent_weather_sunny_20_filled",             WeatherStation = "IND", Value = "84°" },
        new Location { Name = "San Francisco", Icon = "fluent_weather_sunny_20_filled",             WeatherStation = "USA", Value = "69°" },
        new Location { Name = "Nairobi",       Icon = "fluent_weather_rain_20_filled",              WeatherStation = "KEN", Value = "67°" },
        new Location { Name = "Lagos",         Icon = "fluent_weather_partly_cloudy",               WeatherStation = "NGA", Value = "83°" },
    };
}
