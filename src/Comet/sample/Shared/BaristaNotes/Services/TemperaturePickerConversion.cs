using System;

namespace CometBaristaNotes.Services;

public static class TemperaturePickerConversion
{
    public static decimal ToFahrenheitDisplay(decimal celsius) =>
        Math.Round(celsius * 9m / 5m + 32m, 1, MidpointRounding.AwayFromZero);

    public static decimal ToCelsiusStorage(decimal fahrenheit) =>
        Math.Round((fahrenheit - 32m) * 5m / 9m, 2);
}
