namespace WorstHotel.Tests
{
    // Fixed-roster counterfactuals retain authored calendar, heat and guest settings.
    // They explicitly isolate sales; automatic demand has its own production tests.
    static class ManualBookingFixture
    {
        public static OperationsSettings Operations(SessionConfig config)
        {
            var source = config.OperationsData();
            return new OperationsSettings(source.SecondsPerDay, source.StartHour, source.ReportHour,
                source.ArrivalStartHour, source.ArrivalEndHour, source.SleepHour, source.CheckoutHour,
                source.ReportHistoryLimit, sales: new SalesSettings(enabled: false));
        }
    }
}
