namespace ParentalControl.Core.Models;

public class WebFilterTagDomain
{
    public int    Id     { get; set; }
    public int    TagId  { get; set; }
    public string Domain { get; set; } = "";
}
