namespace CardioView.Models;

public sealed class Patient
{
    public int Id { get; set; } = 205;
    public string Name { get; set; } = "José da Silva";
    public string Size { get; set; } = "ADULTO";

    public string DisplayName => $"Paciente {Id} - {Name}";
}
