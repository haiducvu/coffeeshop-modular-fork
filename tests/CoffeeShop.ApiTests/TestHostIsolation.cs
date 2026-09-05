// Each WebApplicationFactory runs the real entry point, which owns the process-global
// Serilog bootstrap logger. Serializing hosts prevents one host's shutdown from
// disposing that logger while another host is still starting.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
