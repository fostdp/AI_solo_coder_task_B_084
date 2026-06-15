
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.OdeSolvers;
using MathNet.Numerics;

namespace TextileMonitoring.API.ODE
{
    public class PopulationState
    {
        public double PestDensity { get; set; }
        public double PredatorDensity { get; set; }
        public double FungiCFU { get; set; }
    }

    public class OdeParameters
    {
        public double r { get; set; } = 0.05;
        public double K { get; set; } = 12.0;
        public double alpha { get; set; } = 0.35;
        public double beta { get; set; } = 0.02;
        public double delta { get; set; } = 0.08;
        public double Kp { get; set; } = 2.5;
        public double rho { get; set; } = 0.015;
        public double Kf { get; set; } = 500.0;
        public double gamma { get; set; } = 0.0003;
        public double TempFactor { get; set; } = 1.0;
        public double HumFactor { get; set; } = 1.0;
    }

    public class OdeSolverResult
    {
        public double Time { get; set; }
        public PopulationState State { get; set; }
        public double SynergyRisk { get; set; }
    }

    public static class OdePopulationSolver
    {
        public static List<OdeSolverResult> SolveLotkaVolterraGompertzCoupled(
            OdeParameters parameters,
            PopulationState initial,
            double totalDays,
            double initialStep = 0.1)
        {
            double envFactor = parameters.TempFactor * parameters.HumFactor;

            Func<double, Vector<double>, Vector<double>> coupledSystem = (t, y) =>
            {
                double N = Math.Max(0.0, y[0]);
                double P = Math.Max(0.0, y[1]);
                double F = Math.Max(0.0, y[2]);

                double r = parameters.r * envFactor;
                double dNdt = r * N * (1.0 - N / parameters.K)
                            - parameters.alpha * N * P
                            - parameters.gamma * N * F * 0.1;

                double pestRatio = N > 0.1 ? 1.0 : 0.3;
                double dPdt = (parameters.beta * N * P - parameters.delta * P) * pestRatio;
                dPdt = Math.Max(dPdt, -P * 0.1);

                double rhoAdj = parameters.rho * envFactor;
                double Fadj = Math.Max(1.0, F);
                double dFdt = F <= 0 ? 0.0 : rhoAdj * F * Math.Log(Math.Max(1.001, parameters.Kf / Fadj))
                              + parameters.beta * 0.001 * N * F * 0.05;

                return Vector<double>.Build.DenseOfArray(new[]
                {
                    dNdt, dPdt, dFdt
                });
            };

            var y0 = Vector<double>.Build.DenseOfArray(new[]
            {
                Math.Max(0.01, initial.PestDensity),
                Math.Max(0.01, initial.PredatorDensity),
                Math.Max(1.0, initial.FungiCFU)
            });

            double h = initialStep;
            double currentTime = 0.0;
            var currentState = y0;
            var results = new List<OdeSolverResult>((int)totalDays + 2)
            {
                new OdeSolverResult
                {
                    Time = 0,
                    State = new PopulationState
                    {
                        PestDensity = y0[0],
                        PredatorDensity = y0[1],
                        FungiCFU = y0[2]
                    },
                    SynergyRisk = CalculateSynergyRisk(y0[0], y0[2])
                }
            };

            while (currentTime < totalDays)
            {
                if (currentTime + h > totalDays)
                    h = totalDays - currentTime;

                double k1 = coupledSystem(currentTime, currentState)[0];
                double k1n = k1; double k1p = coupledSystem(currentTime, currentState)[1];
                double k1f = coupledSystem(currentTime, currentState)[2];

                var y2 = currentState + h * 0.5 * Vector<double>.Build.DenseOfArray(new[] { k1n, k1p, k1f });
                var s2 = coupledSystem(currentTime + h * 0.5, y2);
                double k2n = s2[0]; double k2p = s2[1]; double k2f = s2[2];

                var y3 = currentState + h * 0.5 * Vector<double>.Build.DenseOfArray(new[] { k2n, k2p, k2f });
                var s3 = coupledSystem(currentTime + h * 0.5, y3);
                double k3n = s3[0]; double k3p = s3[1]; double k3f = s3[2];

                var y4 = currentState + h * Vector<double>.Build.DenseOfArray(new[] { k3n, k3p, k3f });
                var s4 = coupledSystem(currentTime + h, y4);
                double k4n = s4[0]; double k4p = s4[1]; double k4f = s4[2];

                var next = currentState + (h / 6.0) * Vector<double>.Build.DenseOfArray(new[]
                {
                    k1n + 2*k2n + 2*k3n + k4n,
                    k1p + 2*k2p + 2*k3p + k4p,
                    k1f + 2*k2f + 2*k3f + k4f
                });

                double growthRatio = currentState[0] > 1e-6
                    ? Math.Abs(next[0] - currentState[0]) / Math.Abs(currentState[0])
                    : 0;

                if (growthRatio > 0.5 || double.IsNaN(next[0]) || double.IsNaN(next[1]) || double.IsNaN(next[2]))
                {
                    h = Math.Max(0.005, h * 0.5);
                    continue;
                }

                currentTime += h;
                currentState = next;
                currentState[0] = Math.Max(0.001, Math.Min(currentState[0], parameters.K * 1.05));
                currentState[1] = Math.Max(0.001, Math.Min(currentState[1], parameters.Kp * 1.1));
                currentState[2] = Math.Max(0.1, Math.Min(currentState[2], parameters.Kf * 1.2));

                int expectedDay = (int)Math.Round(currentTime);
                if (expectedDay > results[^1].Time - 0.001 || Math.Abs(currentTime - expectedDay) < 0.001)
                {
                    results.Add(new OdeSolverResult
                    {
                        Time = Math.Round(currentTime, 3),
                        State = new PopulationState
                        {
                            PestDensity = Math.Round(currentState[0], 4),
                            PredatorDensity = Math.Round(currentState[1], 4),
                            FungiCFU = Math.Round(currentState[2], 2)
                        },
                        SynergyRisk = Math.Round(CalculateSynergyRisk(currentState[0], currentState[2]), 2)
                    });
                }

                if (growthRatio < 0.02 && h < 0.5)
                    h = Math.Min(0.5, h * 1.2);
            }

            return results;
        }

        public static List<OdeSolverResult> SolveFixedStep(
            OdeParameters parameters,
            PopulationState initial,
            double totalDays,
            double fixedStep = 1.0)
        {
            double envFactor = parameters.TempFactor * parameters.HumFactor;

            Func<double, double, double, (double dN, double dP, double dF)> derivatives = (N, P, F) =>
            {
                N = Math.Max(0.0, N); P = Math.Max(0.0, P); F = Math.Max(0.0, F);
                double r = parameters.r * envFactor;
                double dN = r * N * (1.0 - N / parameters.K)
                          - parameters.alpha * N * P
                          - parameters.gamma * N * F * 0.1;

                double pestRatio = N > 0.1 ? 1.0 : 0.3;
                double dP = (parameters.beta * N * P - parameters.delta * P) * pestRatio;
                dP = Math.Max(dP, -P * 0.1);

                double rhoAdj = parameters.rho * envFactor;
                double Fadj = Math.Max(1.0, F);
                double dF = F <= 0 ? 0.0 : rhoAdj * F * Math.Log(Math.Max(1.001, parameters.Kf / Fadj))
                             + parameters.beta * 0.001 * N * F * 0.05;

                return (dN, dP, dF);
            };

            var results = new List<OdeSolverResult>
            {
                new OdeSolverResult
                {
                    Time = 0,
                    State = new PopulationState
                    {
                        PestDensity = Math.Max(0.01, initial.PestDensity),
                        PredatorDensity = Math.Max(0.01, initial.PredatorDensity),
                        FungiCFU = Math.Max(1.0, initial.FungiCFU)
                    },
                    SynergyRisk = CalculateSynergyRisk(Math.Max(0.01, initial.PestDensity), Math.Max(1.0, initial.FungiCFU))
                }
            };

            double Ncur = results[0].State.PestDensity;
            double Pcur = results[0].State.PredatorDensity;
            double Fcur = results[0].State.FungiCFU;

            for (int step = 0; step < (int)Math.Ceiling(totalDays / fixedStep); step++)
            {
                var d = derivatives(Ncur, Pcur, Fcur);
                Ncur += fixedStep * d.dN;
                Pcur += fixedStep * d.dP;
                Fcur += fixedStep * d.dF;
                Ncur = Math.Max(0.001, Ncur);
                Pcur = Math.Max(0.001, Pcur);
                Fcur = Math.Max(0.1, Fcur);

                double t = (step + 1) * fixedStep;
                results.Add(new OdeSolverResult
                {
                    Time = Math.Round(t, 3),
                    State = new PopulationState
                    {
                        PestDensity = Math.Round(Ncur, 4),
                        PredatorDensity = Math.Round(Pcur, 4),
                        FungiCFU = Math.Round(Fcur, 2)
                    },
                    SynergyRisk = Math.Round(CalculateSynergyRisk(Ncur, Fcur), 2)
                });
            }

            return results;
        }

        private static double CalculateSynergyRisk(double H, double F)
        {
            double hNorm = Math.Min(1.0, H / 10.0);
            double fNorm = Math.Min(1.0, F / 500.0);
            double phi = 1.35;
            double alpha = 0.5, beta = 0.35, gamma = 0.15;
            double R = Math.Sqrt(alpha * hNorm * hNorm + beta * fNorm * fNorm + gamma * hNorm * fNorm * phi);
            return Math.Min(100.0, R * 100.0);
        }
    }
}
