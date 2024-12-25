using System;
//.NET 4.8.1
public class CPHInline
{
    public bool Execute()
    {
        // your main code goes here
        SunriseCalculator.CalculateAndPersistLocalSunRiseSunSetTime(out var localSunrise, out var localSunset);
        CPH.SetGlobalVar("SUNRISETIME", localSunrise, true);
        CPH.SetGlobalVar("SUNSETTIME", localSunset, true);
        return true;
    }
}

// https://en.wikipedia.org/wiki/Sunrise_equation#Complete_calculation_on_Earth
public static class SunriseCalculator
{
    private const double EpochReferenceJulianDate = 2451545.0;
    private const double LeapSecondsAdjustment = 0.0008;
    private const double MeanAnomalyAtEpoch = 357.5291;
    private const double DailyIncreaseInAnomaly = 0.98560028;
    private const double PrimaryCorrectionFactorForEllipticalOrbit = 1.9148;
    private const double SecondaryCorrectionFactorForEllipticalOrbit = 0.0200;
    private const double TertiaryCorrectionFactorForEllipticalOrbit = 0.0003;
    private const double ArgumentOfPerihelion = 102.9372;
    private const double EarthOrbitSpeedCorrection = 0.0053;
    private const double EarthAxialTiltCorrection = 0.0069;
    private const double MaximumEarthAxialTilt = 23.4397;
    private const double AtmosphericRefraction = -0.833;

    public const double Longitude; //TODO: Enter Longitude
    public const double Latitude; //TODO: Enter Latitude
    public static void CalculateAndPersistLocalSunRiseSunSetTime(out DateTime localSunrise, out DateTime localSunset)
    {
        var n = CalculateCurrentJulianDay(DateTime.UtcNow);
        var meanSolarTime = CalculateMeanSolarTime(n, 6.788135098816674);
        var solarMeanAnomaly = CalculateSolarMeanAnomaly(meanSolarTime);
        var equationOfTheCenter = CalculateEquationOfTheCenter(solarMeanAnomaly);
        var eclipticLongitude = CalculateEclipticLongitude(solarMeanAnomaly, equationOfTheCenter);
        var solarTransit = CalculateSolarTransit(meanSolarTime, solarMeanAnomaly, eclipticLongitude);
        var declinationOfSun = CalculateDeclinationOfSun(eclipticLongitude);
        var hourAngleDegrees = CalculateHourAngle(50.65224699191153, declinationOfSun);
        var sunriseJulianDate = CalculateSunriseJulianDate(solarTransit, hourAngleDegrees);
        var utcSunrise = JulianDateToDateTime(sunriseJulianDate);
        localSunrise = utcSunrise.ToLocalTime();
        
        var sunsetJulianDate = CalculateSunsetJulianDate(solarTransit, hourAngleDegrees);
        var utcSunset = JulianDateToDateTime(sunsetJulianDate);
        localSunset = utcSunset.ToLocalTime();
    }

    private static DateTime JulianDateToDateTime(double julianDate)
    {
        // Julian date for January 1, 2000, 12:00 TT
        const double julianDate2000 = 2451545.0;
        // DateTime for January 1, 2000, 12:00:00 UTC
        DateTime date2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        // Calculate the difference in days between the given Julian date and the reference Julian date
        double daysDifference = julianDate - julianDate2000;
        // Calculate the corresponding DateTime in UTC
        DateTime utcDateTime = date2000.AddDays(daysDifference);
        return utcDateTime;
    }

    private static double CalculateSunriseJulianDate(double solarTransitJulianDate, double hourAngleDegrees)
    {
        var sunrise = solarTransitJulianDate - (hourAngleDegrees / 360.0);
        //Console.WriteLine("sunrise julian date: " + sunrise);
        return sunrise;
    }

    private static double CalculateSunsetJulianDate(double solarTransitJulianDate, double hourAngleDegrees)
    {
        var sunset = solarTransitJulianDate + (hourAngleDegrees / 360.0);
        //Console.WriteLine("sunset julian date: " + sunrise);
        return sunset;
    }

    private static double CalculateHourAngle(double latitudeDegrees, double solarDeclinationRadians)
    {
        var latitudeRadians = DegreesToRadians(latitudeDegrees);
        var atmosphericRefractionRadians = DegreesToRadians(AtmosphericRefraction);
        // Calculate the cosine of the hour angle (cos(omega_o))
        var cosHourAngle = (Math.Sin(atmosphericRefractionRadians) - Math.Sin(latitudeRadians) * Math.Sin(solarDeclinationRadians)) / (Math.Cos(latitudeRadians) * Math.Cos(solarDeclinationRadians));
        // Ensure the value is within the valid range for arc cos
        cosHourAngle = Math.Max(-1.0, Math.Min(1.0, cosHourAngle));
        // Calculate the hour angle in radians
        var hourAngleRadians = Math.Acos(cosHourAngle);
        // Convert the hour angle to degrees
        var hourAngleDegrees = RadiansToDegrees(hourAngleRadians);
        //Console.WriteLine("hour angle degrees: " + hourAngleDegrees);
        return hourAngleDegrees;
    }

    private static double CalculateDeclinationOfSun(double eclipticLongitudeDegrees)
    {
        var eclipticLongitudeRadians = DegreesToRadians(eclipticLongitudeDegrees);
        var declinationOfSun = Math.Sin(eclipticLongitudeRadians) * Math.Sin(DegreesToRadians(MaximumEarthAxialTilt));
        //Console.WriteLine("declination of the sun: "+ declinationOfSun);
        return declinationOfSun;
    }

    private static double CalculateSolarTransit(double meanSolarTime, double meanAnomalyDegrees, double eclipticLongitudeDegrees)
    {
        // Convert mean anomaly and ecliptic longitude from degrees to radians
        var meanAnomalyRadians = DegreesToRadians(meanAnomalyDegrees);
        var eclipticLongitudeRadians = DegreesToRadians(eclipticLongitudeDegrees);
        // Calculate the Equation of Time correction
        var equationOfTime = EarthOrbitSpeedCorrection * Math.Sin(meanAnomalyRadians) - EarthAxialTiltCorrection * Math.Sin(2 * eclipticLongitudeRadians);
        var solarTransit = EpochReferenceJulianDate + meanSolarTime + equationOfTime;
        //Console.WriteLine("solar transit: " + solarTransit);
        return solarTransit;
    }

    private static double CalculateEclipticLongitude(double meanAnomaly, double equationOfTheCenter)
    {
        var eclipticLongitude = (meanAnomaly + equationOfTheCenter + 180 + ArgumentOfPerihelion) % 360;
        //Console.WriteLine("ecliptic longitude: " + eclipticLongitude);
        return eclipticLongitude;
    }

    private static double CalculateEquationOfTheCenter(double meanAnomalyDegrees)
    {
        var meanAnomalyRadians = DegreesToRadians(meanAnomalyDegrees);
        var equationToCenter = PrimaryCorrectionFactorForEllipticalOrbit * Math.Sin(meanAnomalyRadians) + SecondaryCorrectionFactorForEllipticalOrbit * Math.Sin(2 * meanAnomalyRadians) + TertiaryCorrectionFactorForEllipticalOrbit * Math.Sin(3 * meanAnomalyRadians);
        //Console.WriteLine("equation of the center: " + equationToCenter);
        return equationToCenter;
    }

    private static double CalculateSolarMeanAnomaly(double meanSolarTime)
    {
        var meanSolarAnomaly = (MeanAnomalyAtEpoch + DailyIncreaseInAnomaly * meanSolarTime) % 360;
        //Console.WriteLine("solar mean anomaly: " + meanSolarAnomaly);
        return meanSolarAnomaly;
    }

    private static double CalculateMeanSolarTime(int julianDay, double longitudeDegrees)
    {
        var meanSolarTime = julianDay - (longitudeDegrees / 360.0);
        //Console.WriteLine("mean solar time: " + meanSolarTime);
        return meanSolarTime;
    }

    private static double GetCurrentJulianDate(DateTime dateTime)
    {
        var year = dateTime.Year;
        var month = dateTime.Month;
        var day = dateTime.Day + dateTime.Hour / 24.0 + dateTime.Minute / 1440.0 + dateTime.Second / 86400.0;
        // If the month is January or February, subtract 1 from the year and add 12 to the month
        if (month <= 2)
        {
            year -= 1;
            month += 12;
        }

        var century = year / 100;
        var leapYearCorrection = 2 - century + century / 4;
        var julianDay = Math.Floor(365.25 * (year + 4716)) + Math.Floor(30.6001 * (month + 1)) + day + leapYearCorrection - 1524.5;
        return julianDay;
    }

    private static int CalculateCurrentJulianDay(DateTime dateTime)
    {
        var julianDate = GetCurrentJulianDate(dateTime);
        //Console.WriteLine("julian date: " + julianDate);
        var daysSinceReferenceJulianDate = (int)Math.Ceiling(julianDate - EpochReferenceJulianDate + LeapSecondsAdjustment);
        //Console.WriteLine("days since reference julian date: " + daysSinceReferenceJulianDate);
        return daysSinceReferenceJulianDate;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }
}
