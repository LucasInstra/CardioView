namespace CardioView.Models;

public sealed class VitalSigns
{
    public double HeartRate;
    public double Spo2;
    public double Systolic;
    public double Diastolic;
    public double RespiratoryRate;
    public double Temp1;
    public double Temp2;
    public double EtCo2;
    public double Fico2 = 10.0;

    public double Map => (Systolic + 2 * Diastolic) / 3.0;
}
