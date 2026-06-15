
namespace TextileMonitoring.Contracts.RabbitMQ;

public static class QueueNames
{
    public const string SensorData = "textile.sensor_data";
    public const string PopulationPrediction = "textile.population_prediction";
    public const string MildewPrediction = "textile.mildew_prediction";
    public const string AlertTrigger = "textile.alert_trigger";
    public const string AlertDispatch = "textile.alert_dispatch";
    public const string PestClassification = "textile.pest_classification";
    public const string VocSensor = "textile.voc_sensor";
    public const string VocClassification = "textile.voc_classification";
    public const string TreatmentRequest = "textile.treatment_request";
    public const string TreatmentResult = "textile.treatment_result";
    public const string VulnerabilityAssessment = "textile.vulnerability_assessment";

    public static class Exchanges
    {
        public const string Sensor = "textile.exchange.sensor";
        public const string Prediction = "textile.exchange.prediction";
        public const string Alert = "textile.exchange.alert";
        public const string Classification = "textile.exchange.classification";
        public const string Treatment = "textile.exchange.treatment";
        public const string Vulnerability = "textile.exchange.vulnerability";
    }

    public static class RoutingKeys
    {
        public const string DustSensor = "sensor.dust";
        public const string FungiSensor = "sensor.fungi";
        public const string VocSensor = "sensor.voc";
        public const string FrassImage = "sensor.frass_image";
        public const string Population = "prediction.population";
        public const string Mildew = "prediction.mildew";
        public const string Synergy = "prediction.synergy";
        public const string PestClassified = "classify.pest";
        public const string VocClassified = "classify.voc";
        public const string TreatmentSubmit = "treatment.submit";
        public const string TreatmentDone = "treatment.done";
        public const string VulnerabilityUpdate = "vulnerability.update";
    }
}
