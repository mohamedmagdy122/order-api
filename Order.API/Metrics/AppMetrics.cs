using Prometheus;

namespace Order.API.Metrics;

public static class AppMetrics
{
    public static readonly Counter OrderCreatedTotal = Prometheus.Metrics
        .CreateCounter("order_created_total", "Total orders created.",
            new CounterConfiguration { LabelNames = new[] { "status" } });

    public static readonly Counter OrderValueTotal = Prometheus.Metrics
        .CreateCounter("order_value_total", "Running sum of order amounts.");

    public static readonly Histogram ProcessingDuration = Prometheus.Metrics
        .CreateHistogram("order_processing_duration_seconds", "Time to process an order.",
            new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.01, 2, 10) });

    public static readonly Histogram ItemsPerOrder = Prometheus.Metrics
        .CreateHistogram("order_items_per_order", "Number of items per order.",
            new HistogramConfiguration { Buckets = new double[] { 1, 2, 3, 5, 8, 10, 15, 20, 50 } });
}
