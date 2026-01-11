using NetTopologySuite.Geometries;

namespace TollService.Domain.WeighStations;

public class WeighStation
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Web { get; set; } = string.Empty;
    public Point? Location { get; set; }

    public WeighStation()
    {
    }

    public WeighStation(string title, string address, string web, Point? location)
    {
        Id = Guid.NewGuid();
        Title = title;
        Address = address;
        Web = web;
        Location = location;
    }
}

