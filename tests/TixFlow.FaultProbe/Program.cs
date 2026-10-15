using System.Text.Json;
using RabbitMQ.Client;
using TixFlow.Infrastructure.Processing;

// Test-only consumer process. Parent kills it at the DB-commit / Rabbit-ack boundary.
// No fault switches or endpoints are added to the production worker.
string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name);
using var connection = new ConnectionFactory
{
    HostName = Required("PROBE_BROKER_HOST"), Port = int.Parse(Required("PROBE_BROKER_PORT"), System.Globalization.CultureInfo.InvariantCulture),
    UserName = "tixflow_test", Password = "isolated_test_only", AutomaticRecoveryEnabled = false
}.CreateConnection("phase8-crash-probe");
using var channel = connection.CreateModel();
var received = channel.BasicGet(RabbitDelivery.Queue, false) ?? throw new InvalidOperationException("Expected delivery");
var delivery = JsonSerializer.Deserialize<Delivery>(received.Body.Span) ?? throw new InvalidOperationException("Expected envelope");
await new TicketDelivery(Required("PROBE_POSTGRES")).ProcessAsync(delivery);
Console.WriteLine("COMMITTED_WITHOUT_ACK");
await Console.Out.FlushAsync();
await Task.Delay(Timeout.InfiniteTimeSpan);
