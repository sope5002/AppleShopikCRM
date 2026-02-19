public class RepairService
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public decimal CostPrice { get; set; }
    public string? CompatibleModel { get; set; } // Null = для всіх
}