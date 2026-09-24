using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FlightLogger
{
    internal class FlightLoggerBehaviour : MonoBehaviour
    {
        private sealed class CsvRecord
        {
            public CsvRecord(double? missionTime, string line)
            {
                MissionTime = missionTime;
                Line = line;
            }

            public double? MissionTime { get; }
            public string Line { get; }
        }

        private static readonly string[] Header =
        {
            "time_s", "mission_time_s", "native_event", "current_body",
            "body_radius_m", "local_gravity_mps2",
            "altitude_m", "terrain_altitude_m", "speed_mps", "velocity_x", "velocity_y",
            "position_x", "position_y", "vertical_speed_mps", "horizontal_speed_mps", "angle_deg",
            "velocity_angle_deg",
            "acceleration_x", "acceleration_y", "acceleration_mps2",
            "mass_t", "thrust", "throttle", "fuel_percent", "twr",
            "apoapsis_m", "periapsis_m", "apoapsis_altitude_m", "periapsis_altitude_m",
            "orbit_eccentricity", "semi_major_axis_m",
            "is_engine_on", "is_parachute_deployed", "is_in_atmosphere", "is_landed",
            "active_part_count"
        };

        private readonly HashSet<string> warnedMissingFields = new HashSet<string>();
        private readonly List<CsvRecord> writtenRows = new List<CsvRecord>();
        private Entrypoint mod;
        private string logDirectory;
        private string csvPath;
        private StreamWriter writer;
        private float startTime;
        private float nextSampleTime;
        private float nextFlushTime;
        private bool inspected;
        private int lineCount;
        private double? previousMissionTime;
        private double? previousLoggerTime;
        private double? previousVelocityX;
        private double? previousVelocityY;
        private string previousPlanet;
        private string previousOrbitState;
        private string previousAtmosphereState;
        private bool? previousLanded;

        public void Initialize(Entrypoint entrypoint)
        {
            mod = entrypoint;
        }

        private void Start()
        {
            logDirectory = ResolveLogDirectory();
            Directory.CreateDirectory(logDirectory);
            Debug.Log("[FlightLogger] Started");
        }

        private void Update()
        {
            object rocket = GetCurrentRocket();
            if (rocket == null)
                return;

            if (!inspected)
            {
                WriteDebugInspector(rocket);
                OpenCsv();
                inspected = true;
            }

            if (writer == null || Time.unscaledTime < nextSampleTime)
                return;

            nextSampleTime += 0.1f;
            WriteTelemetryLine(rocket);

            if (Time.unscaledTime >= nextFlushTime)
            {
                writer.Flush();
                nextFlushTime = Time.unscaledTime + 1f;
            }
        }

        private void OnDestroy()
        {
            CloseCsv();
        }

        private string ResolveLogDirectory()
        {
            string modFolder = mod != null ? mod.ModFolder : null;
            if (!string.IsNullOrEmpty(modFolder))
            {
                string appRoot = Path.GetFullPath(Path.Combine(modFolder, "..", ".."));
                return Path.Combine(appRoot, "FlightLogs");
            }

            string defaultApp = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                "Library/Application Support/Steam/steamapps/common/Spaceflight Simulator/SpaceflightSimulatorGame.app");

            if (Directory.Exists(defaultApp))
                return Path.Combine(defaultApp, "FlightLogs");

            return Path.Combine(Directory.GetCurrentDirectory(), "FlightLogs");
        }

        private void OpenCsv()
        {
            string fileName = "flight_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv";
            csvPath = Path.Combine(logDirectory, fileName);
            writtenRows.Clear();
            writer = new StreamWriter(csvPath, false, new UTF8Encoding(false));
            writer.WriteLine(string.Join(",", Header));
            writer.Flush();
            startTime = Time.unscaledTime;
            nextSampleTime = Time.unscaledTime;
            nextFlushTime = Time.unscaledTime + 1f;
            Debug.Log("[FlightLogger] CSV file: " + csvPath);
        }

        private void CloseCsv()
        {
            if (writer == null)
                return;

            writer.Flush();
            writer.Dispose();
            writer = null;
            Debug.Log("[FlightLogger] Closed file");
        }

        private object GetCurrentRocket()
        {
            return SafeRead("current_rocket", () =>
            {
                Type controllerType = ReflectionTools.Type("SFS.World.PlayerController");
                object controller = ReflectionTools.GetMember(controllerType, "main");
                object playerLocal = ReflectionTools.GetMember(controller, "player");
                object player = ReflectionTools.GetValueObject(playerLocal);
                if (player != null && player.GetType().FullName == "SFS.World.Rocket")
                    return player;

                Type gameManagerType = ReflectionTools.Type("SFS.World.GameManager");
                object gameManager = ReflectionTools.GetMember(gameManagerType, "main");
                object rockets = ReflectionTools.GetMember(gameManager, "rockets");
                return (rockets as System.Collections.IEnumerable)?.Cast<object>().FirstOrDefault();
            });
        }

        private void WriteDebugInspector(object rocket)
        {
            string path = Path.Combine(logDirectory, "debug_objects.txt");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[FlightLogger] Debug Inspector");
            sb.AppendLine(DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            sb.AppendLine();

            object locationHolder = SafeRead("rocket.location", () => ReflectionTools.GetMember(rocket, "location"));
            object location = SafeRead("rocket.location.Value", () => ReflectionTools.GetValueObject(locationHolder));
            object planet = SafeRead("location.planet", () => ReflectionTools.GetMember(location, "planet"));
            object orbit = SafeRead("orbit", () => ReflectionTools.TryCreateOrbit(location));
            object mass = SafeRead("rocket.mass", () => ReflectionTools.GetMember(rocket, "mass"));
            object throttle = SafeRead("rocket.throttle", () => ReflectionTools.GetMember(rocket, "throttle"));
            object resources = SafeRead("rocket.resources", () => ReflectionTools.GetMember(rocket, "resources"));
            object partHolder = SafeRead("rocket.partHolder", () => ReflectionTools.GetMember(rocket, "partHolder"));
            object stats = SafeRead("rocket.stats", () => ReflectionTools.GetMember(rocket, "stats"));

            Dump(sb, "PlayerController.main", SafeRead("PlayerController.main", () => ReflectionTools.GetMember(ReflectionTools.Type("SFS.World.PlayerController"), "main")));
            Dump(sb, "current rocket", rocket);
            Dump(sb, "location", location);
            Dump(sb, "planet/body", planet);
            Dump(sb, "orbit", orbit);
            Dump(sb, "mass", mass);
            Dump(sb, "throttle", throttle);
            Dump(sb, "resources", resources);
            Dump(sb, "partHolder", partHolder);
            Dump(sb, "stats", stats);
            Dump(sb, "LogManager.main", SafeRead("LogManager.main", () => ReflectionTools.GetMember(ReflectionTools.Type("SFS.Stats.LogManager"), "main")));

            DumpModuleGroup(sb, partHolder, "EngineModule", "SFS.Parts.Modules.EngineModule");
            DumpModuleGroup(sb, partHolder, "BoosterModule", "SFS.Parts.Modules.BoosterModule");
            DumpModuleGroup(sb, partHolder, "ResourceModule", "SFS.Parts.Modules.ResourceModule");
            DumpModuleGroup(sb, partHolder, "ParachuteModule", "SFS.Parts.Modules.ParachuteModule");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[FlightLogger] Debug inspector wrote: " + path);
        }

        private void Dump(StringBuilder sb, string title, object value)
        {
            sb.AppendLine("## " + title);
            sb.AppendLine(ReflectionTools.DescribeObject(value));
            sb.AppendLine();
        }

        private void DumpModuleGroup(StringBuilder sb, object partHolder, string title, string typeName)
        {
            Array modules = SafeRead(title, () => ReflectionTools.GetModules(partHolder, typeName));
            sb.AppendLine("## " + title);
            sb.AppendLine("count = " + (modules == null ? 0 : modules.Length));
            if (modules != null)
            {
                int index = 0;
                foreach (object module in modules.Cast<object>().Take(5))
                {
                    sb.AppendLine("-- " + title + "[" + index++ + "]");
                    sb.AppendLine(ReflectionTools.DescribeObject(module, 40));
                }
            }
            sb.AppendLine();
        }

        private void WriteTelemetryLine(object rocket)
        {
            object locationHolder = SafeRead("rocket.location", () => ReflectionTools.GetMember(rocket, "location"));
            object location = SafeRead("rocket.location.Value", () => ReflectionTools.GetValueObject(locationHolder));
            object planet = SafeRead("location.planet", () => ReflectionTools.GetMember(location, "planet"));
            object position = SafeRead("location.position", () => ReflectionTools.GetMember(location, "position"));
            object velocity = SafeRead("location.velocity", () => ReflectionTools.GetMember(location, "velocity"));
            object orbit = SafeRead("orbit", () => ReflectionTools.TryCreateOrbit(location));

            double time = Time.unscaledTime - startTime;
            double? missionTime = ReadDouble("mission_time_s", () => ReflectionTools.GetMember(location, "time"));
            if (missionTime.HasValue && previousMissionTime.HasValue && missionTime.Value < previousMissionTime.Value - 0.25)
                HandleMissionTimeRewind(missionTime.Value);

            string body = ReadBodyName(planet);
            double? bodyRadius = ReadDouble("body_radius_m", () => ReflectionTools.GetMember(planet, "Radius"));
            double? altitude = ReadDouble("altitude_m", () => ReflectionTools.GetMember(location, "Height"));
            double? terrainAltitude = SafeRead<double?>("terrain_altitude_m", () =>
            {
                double? terrainHeight = InvokeTerrainHeight(location);
                if (!terrainHeight.HasValue || !altitude.HasValue)
                {
                    WarnMissing("terrain_altitude_m");
                    return null;
                }

                return altitude.Value - terrainHeight.Value;
            });

            double? velocityX = ReadDouble("velocity_x", () => ReflectionTools.X(velocity));
            double? velocityY = ReadDouble("velocity_y", () => ReflectionTools.Y(velocity));
            double? speed = ReadDouble("speed_mps", () => ReflectionTools.Magnitude(velocity));
            double? positionX = ReadDouble("position_x", () => ReflectionTools.X(position));
            double? positionY = ReadDouble("position_y", () => ReflectionTools.Y(position));
            double? verticalSpeed = ReadDouble("vertical_speed_mps", () => ReflectionTools.GetMember(location, "VerticalVelocity"));
            double? horizontalSpeed = CalculateHorizontalSpeed(speed, verticalSpeed);
            double? velocityAngle = CalculateVelocityAngle(velocityX, velocityY);
            double? angle = SafeRead<double?>("angle_deg", () =>
            {
                double? value = ReflectionTools.AsDouble(InvokeGetRotation(rocket)) ?? velocityAngle;
                if (!value.HasValue)
                    WarnMissing("angle_deg");
                return value;
            });

            double? accelerationX = null;
            double? accelerationY = null;
            double? acceleration = null;
            if (previousVelocityX.HasValue && previousVelocityY.HasValue && previousLoggerTime.HasValue &&
                velocityX.HasValue && velocityY.HasValue)
            {
                double dt = missionTime.HasValue && previousMissionTime.HasValue
                    ? missionTime.Value - previousMissionTime.Value
                    : 0;
                if (Math.Abs(dt) <= 1e-6)
                    dt = time - previousLoggerTime.Value;

                if (Math.Abs(dt) > 1e-6)
                {
                    accelerationX = (velocityX.Value - previousVelocityX.Value) / dt;
                    accelerationY = (velocityY.Value - previousVelocityY.Value) / dt;
                    acceleration = Math.Sqrt(accelerationX.Value * accelerationX.Value + accelerationY.Value * accelerationY.Value);
                }
            }

            double? mass = ReadDouble("mass_t", () => InvokeNoArg(ReflectionTools.GetMember(rocket, "mass"), "GetMass"));
            double? throttle = ReadDouble("throttle", () => ReflectionTools.GetMember(ReflectionTools.GetMember(rocket, "throttle"), "throttlePercent"));
            double? thrust = ReadThrust(rocket, throttle);
            double? fuelPercent = ReadFuelPercent(rocket);
            double? localGravity = CalculateLocalGravity(planet, position);
            double? twr = CalculateTwr(mass, thrust, localGravity);

            double? apoapsis = ReadDouble("apoapsis_m", () => ReflectionTools.GetMember(orbit, "apoapsis"));
            double? periapsis = ReadDouble("periapsis_m", () => ReflectionTools.GetMember(orbit, "periapsis"));
            double? apoapsisAltitude = apoapsis.HasValue && bodyRadius.HasValue ? apoapsis.Value - bodyRadius.Value : (double?)null;
            double? periapsisAltitude = periapsis.HasValue && bodyRadius.HasValue ? periapsis.Value - bodyRadius.Value : (double?)null;
            double? eccentricity = ReadDouble("orbit_eccentricity", () => ReflectionTools.GetMember(orbit, "ecc"));
            double? semiMajorAxis = ReadDouble("semi_major_axis_m", () => ReflectionTools.GetMember(orbit, "sma"));

            bool? engineOn = ReadEngineOn(rocket);
            bool? parachute = ReadParachuteDeployed(rocket);
            bool? inAtmosphere = ReadBool("is_in_atmosphere", () => InvokeIsInsideAtmosphere(planet, position));
            bool? landed = ReadBool("is_landed", () => ReflectionTools.GetMember(rocket, "IsOnSurface"));
            int? partCount = ReadPartCount(rocket);
            string nativeEvent = ReadNativeEvent(rocket, body, inAtmosphere, landed);

            string[] row =
            {
                CsvUtil.Cell(time), CsvUtil.Cell(missionTime), CsvUtil.Cell(nativeEvent), CsvUtil.Cell(body),
                CsvUtil.Cell(bodyRadius), CsvUtil.Cell(localGravity),
                CsvUtil.Cell(altitude), CsvUtil.Cell(terrainAltitude), CsvUtil.Cell(speed), CsvUtil.Cell(velocityX), CsvUtil.Cell(velocityY),
                CsvUtil.Cell(positionX), CsvUtil.Cell(positionY), CsvUtil.Cell(verticalSpeed), CsvUtil.Cell(horizontalSpeed), CsvUtil.Cell(angle),
                CsvUtil.Cell(velocityAngle),
                CsvUtil.Cell(accelerationX), CsvUtil.Cell(accelerationY), CsvUtil.Cell(acceleration),
                CsvUtil.Cell(mass), CsvUtil.Cell(thrust), CsvUtil.Cell(throttle), CsvUtil.Cell(fuelPercent), CsvUtil.Cell(twr),
                CsvUtil.Cell(apoapsis), CsvUtil.Cell(periapsis), CsvUtil.Cell(apoapsisAltitude), CsvUtil.Cell(periapsisAltitude),
                CsvUtil.Cell(eccentricity), CsvUtil.Cell(semiMajorAxis),
                CsvUtil.Cell(engineOn), CsvUtil.Cell(parachute), CsvUtil.Cell(inAtmosphere), CsvUtil.Cell(landed),
                CsvUtil.Cell(partCount)
            };

            string line = string.Join(",", row);
            writer.WriteLine(line);
            writtenRows.Add(new CsvRecord(missionTime, line));
            lineCount++;
            if (lineCount == 1 || lineCount % 100 == 0)
                Debug.Log("[FlightLogger] Wrote CSV line " + lineCount);

            previousMissionTime = missionTime;
            previousLoggerTime = time;
            previousVelocityX = velocityX;
            previousVelocityY = velocityY;
        }

        private void HandleMissionTimeRewind(double newMissionTime)
        {
            if (writer == null || string.IsNullOrEmpty(csvPath))
                return;

            writer.Flush();
            writer.Dispose();

            int oldCount = writtenRows.Count;
            writtenRows.RemoveAll(row => row.MissionTime.HasValue && row.MissionTime.Value > newMissionTime + 1e-6);

            using (StreamWriter rewrite = new StreamWriter(csvPath, false, new UTF8Encoding(false)))
            {
                rewrite.WriteLine(string.Join(",", Header));
                foreach (CsvRecord row in writtenRows)
                    rewrite.WriteLine(row.Line);
            }

            writer = new StreamWriter(csvPath, true, new UTF8Encoding(false));
            lineCount = writtenRows.Count;
            previousMissionTime = null;
            previousLoggerTime = null;
            previousVelocityX = null;
            previousVelocityY = null;
            previousPlanet = null;
            previousOrbitState = null;
            previousAtmosphereState = null;
            previousLanded = null;

            Debug.Log("[FlightLogger] Mission time rewind detected; removed " + (oldCount - writtenRows.Count) + " CSV rows");
        }

        private string ReadBodyName(object planet)
        {
            return SafeRead("current_body", () =>
            {
                string codeName = ReflectionTools.AsString(ReflectionTools.GetMember(planet, "codeName"));
                if (!string.IsNullOrEmpty(codeName))
                    return codeName;

                object displayName = ReflectionTools.GetMember(planet, "DisplayName");
                return displayName != null ? displayName.ToString() : null;
            });
        }

        private double? InvokeTerrainHeight(object location)
        {
            object result = InvokeNoArgOrOneBool(location, "GetTerrainHeight", true);
            return ReflectionTools.AsDouble(result);
        }

        private object InvokeGetRotation(object rocket)
        {
            return InvokeNoArg(rocket, "GetRotation");
        }

        private object InvokeIsInsideAtmosphere(object planet, object position)
        {
            if (planet == null || position == null)
                return null;

            var method = planet.GetType().GetMethod("IsInsideAtmosphere",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            return method != null ? method.Invoke(planet, new[] { position }) : null;
        }

        private object InvokeNoArg(object target, string methodName)
        {
            if (target == null)
                return null;

            var method = target.GetType().GetMethod(methodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            return method != null ? method.Invoke(target, null) : null;
        }

        private object InvokeNoArgOrOneBool(object target, string methodName, bool arg)
        {
            if (target == null)
                return null;

            var method = target.GetType().GetMethods(System.Reflection.BindingFlags.Instance |
                                                     System.Reflection.BindingFlags.Public |
                                                     System.Reflection.BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 1);
            return method != null ? method.Invoke(target, new object[] { arg }) : null;
        }

        private double? ReadThrust(object rocket, double? rocketThrottle)
        {
            return SafeRead("thrust", () =>
            {
                object partHolder = SafeGetMember(rocket, "partHolder");
                double total = 0;
                bool found = false;

                Array engines = SafeRead<Array>("engine_modules", () => ReflectionTools.GetModules(partHolder, "SFS.Parts.Modules.EngineModule"));
                if (engines != null)
                {
                    foreach (object engine in engines)
                    {
                        try
                        {
                            bool on = SafeAsBool(engine, "engineOn") ?? false;
                            double throttleOut = SafeAsDouble(engine, "throttle_Out") ?? 0;
                            if (throttleOut <= 0)
                                throttleOut = SafeAsDouble(engine, "throttle_Input") ?? 0;
                            if (throttleOut <= 0 && on && rocketThrottle.HasValue)
                                throttleOut = rocketThrottle.Value;

                            double nominal = ReadComposedFloat(SafeGetMember(engine, "thrust")) ?? EstimateEngineThrust(engine);
                            if (on && throttleOut > 0 && nominal > 0)
                                total += nominal * throttleOut;
                            found = true;
                        }
                        catch (Exception ex)
                        {
                            WarnMissing("thrust.engine (" + ex.GetType().Name + ")");
                        }
                    }
                }

                Array boosters = SafeRead<Array>("booster_modules", () => ReflectionTools.GetModules(partHolder, "SFS.Parts.Modules.BoosterModule"));
                if (boosters != null)
                {
                    foreach (object booster in boosters)
                    {
                        try
                        {
                            double throttleValue = SafeAsDouble(booster, "Throttle") ?? 0;
                            object thrustVector = SafeGetMember(SafeGetMember(booster, "thrustVector"), "Value");
                            double nominal = ReflectionTools.Magnitude(thrustVector) ?? 0;
                            if (throttleValue > 0)
                                total += nominal * throttleValue;
                            found = true;
                        }
                        catch (Exception ex)
                        {
                            WarnMissing("thrust.booster (" + ex.GetType().Name + ")");
                        }
                    }
                }

                return found ? (double?)total : 0;
            });
        }

        private double? CalculateLocalGravity(object planet, object position)
        {
            return SafeRead<double?>("local_gravity_mps2", () =>
            {
                double? mu = ReflectionTools.AsDouble(ReflectionTools.GetMember(planet, "mass"));
                double? radius = ReflectionTools.Magnitude(position);
                if (!mu.HasValue || !radius.HasValue || radius.Value <= 0)
                {
                    WarnMissing("local_gravity_mps2");
                    return null;
                }

                return mu.Value / (radius.Value * radius.Value);
            });
        }

        private double? CalculateTwr(double? mass, double? thrust, double? localGravity)
        {
            return SafeRead<double?>("twr", () =>
            {
                if (!mass.HasValue || !thrust.HasValue || !localGravity.HasValue || mass.Value <= 0 || localGravity.Value <= 0)
                    return null;

                return thrust.Value / (mass.Value * localGravity.Value);
            });
        }

        private double? ReadComposedFloat(object composed)
        {
            if (composed == null)
                return null;

            return SafeAsDouble(composed, "Value") ??
                   SafeAsDouble(composed, "value") ??
                   ReflectionTools.AsDouble(SafeRead<object>("composed.GetResult", () => InvokeNoArg(composed, "GetResult")));
        }

        private double EstimateEngineThrust(object engine)
        {
            string name = ReflectionTools.AsString(ReflectionTools.GetMember(engine, "name")) ??
                          ReflectionTools.AsString(ReflectionTools.GetMember(ReflectionTools.GetMember(engine, "gameObject"), "name"));

            if (string.IsNullOrEmpty(name))
                return 0;

            if (name.IndexOf("Valiant", StringComparison.OrdinalIgnoreCase) >= 0)
                return 80;
            if (name.IndexOf("Frontier", StringComparison.OrdinalIgnoreCase) >= 0)
                return 100;
            if (name.IndexOf("Hawk", StringComparison.OrdinalIgnoreCase) >= 0)
                return 120;
            if (name.IndexOf("Kolibri", StringComparison.OrdinalIgnoreCase) >= 0)
                return 15;
            if (name.IndexOf("Titan", StringComparison.OrdinalIgnoreCase) >= 0)
                return 400;
            if (name.IndexOf("Acarii", StringComparison.OrdinalIgnoreCase) >= 0)
                return 12;

            return 0;
        }

        private object SafeGetMember(object target, string member)
        {
            return SafeRead<object>(member, () => ReflectionTools.GetMember(target, member));
        }

        private double? SafeAsDouble(object target, string member)
        {
            return SafeRead<double?>(member, () => ReflectionTools.AsDouble(ReflectionTools.GetMember(target, member)));
        }

        private bool? SafeAsBool(object target, string member)
        {
            return SafeRead<bool?>(member, () => ReflectionTools.AsBool(ReflectionTools.GetMember(target, member)));
        }

        private double? ReadFuelPercent(object rocket)
        {
            return SafeRead<double?>("fuel_percent", () =>
            {
                object partHolder = ReflectionTools.GetMember(rocket, "partHolder");
                Array resources = ReflectionTools.GetModules(partHolder, "SFS.Parts.Modules.ResourceModule");
                if (resources == null || resources.Length == 0)
                    return null;

                double totalAmount = 0;
                double totalCapacity = 0;
                double percentSum = 0;
                int percentCount = 0;

                foreach (object resource in resources)
                {
                    double? amount = ReflectionTools.AsDouble(ReflectionTools.GetMember(resource, "ResourceAmount"));
                    double? capacity = ReflectionTools.AsDouble(ReflectionTools.GetMember(resource, "TotalResourceCapacity"));
                    if (amount.HasValue && capacity.HasValue && capacity.Value > 0)
                    {
                        totalAmount += amount.Value;
                        totalCapacity += capacity.Value;
                    }

                    double? percent = ReflectionTools.AsDouble(ReflectionTools.GetMember(resource, "resourcePercent"));
                    if (percent.HasValue)
                    {
                        percentSum += percent.Value;
                        percentCount++;
                    }
                }

                if (totalCapacity > 0)
                    return totalAmount / totalCapacity * 100.0;

                if (percentCount > 0)
                    return percentSum / percentCount;

                return null;
            });
        }

        private bool? ReadEngineOn(object rocket)
        {
            return SafeRead("is_engine_on", () =>
            {
                object partHolder = ReflectionTools.GetMember(rocket, "partHolder");
                Array engines = ReflectionTools.GetModules(partHolder, "SFS.Parts.Modules.EngineModule");
                if (engines != null && engines.Cast<object>().Any(e => ReflectionTools.AsBool(ReflectionTools.GetMember(e, "engineOn")) == true))
                    return true;

                Array boosters = ReflectionTools.GetModules(partHolder, "SFS.Parts.Modules.BoosterModule");
                if (boosters != null && boosters.Cast<object>().Any(b => (ReflectionTools.AsDouble(ReflectionTools.GetMember(b, "Throttle")) ?? 0) > 0))
                    return true;

                return false;
            });
        }

        private bool? ReadParachuteDeployed(object rocket)
        {
            return SafeRead("is_parachute_deployed", () =>
            {
                object partHolder = ReflectionTools.GetMember(rocket, "partHolder");
                Array parachutes = ReflectionTools.GetModules(partHolder, "SFS.Parts.Modules.ParachuteModule");
                if (parachutes == null)
                    return false;

                return parachutes.Cast<object>().Any(p => (ReflectionTools.AsDouble(ReflectionTools.GetMember(p, "state")) ?? 0) > 0 ||
                                                          (ReflectionTools.AsDouble(ReflectionTools.GetMember(p, "targetState")) ?? 0) > 0);
            });
        }

        private int? ReadPartCount(object rocket)
        {
            return SafeRead("active_part_count", () =>
            {
                object partHolder = ReflectionTools.GetMember(rocket, "partHolder");
                object parts = ReflectionTools.GetMember(partHolder, "parts");
                if (parts is System.Collections.ICollection collection)
                    return collection.Count;

                Array array = InvokeNoArg(partHolder, "GetArray") as Array;
                return array?.Length;
            });
        }

        private string ReadNativeEvent(object rocket, string body, bool? inAtmosphere, bool? landed)
        {
            return SafeRead("native_event", () =>
            {
                List<string> events = new List<string>();
                if (!string.IsNullOrEmpty(previousPlanet) && body != previousPlanet)
                    events.Add("changed_soi:" + body);
                previousPlanet = body;

                object stats = ReflectionTools.GetMember(rocket, "stats");
                object tracker = ReflectionTools.GetMember(stats, "tracker");
                string orbitState = ReflectionTools.AsString(ReflectionTools.GetMember(tracker, "state_Orbit"));
                string atmosphereState = ReflectionTools.AsString(ReflectionTools.GetMember(tracker, "state_Atmosphere"));

                if (!string.IsNullOrEmpty(previousOrbitState) && orbitState != previousOrbitState)
                    events.Add("orbit:" + orbitState);
                if (!string.IsNullOrEmpty(previousAtmosphereState) && atmosphereState != previousAtmosphereState)
                    events.Add("atmosphere:" + atmosphereState);
                if (previousLanded.HasValue && landed == true && previousLanded == false)
                    events.Add("landed");
                if (previousLanded.HasValue && landed == false && previousLanded == true)
                    events.Add("takeoff");

                previousOrbitState = orbitState;
                previousAtmosphereState = atmosphereState;
                previousLanded = landed;

                return events.Count == 0 ? null : string.Join("|", events);
            });
        }

        private double? CalculateHorizontalSpeed(double? speed, double? verticalSpeed)
        {
            if (!speed.HasValue || !verticalSpeed.HasValue)
                return null;

            double value = speed.Value * speed.Value - verticalSpeed.Value * verticalSpeed.Value;
            return value > 0 ? Math.Sqrt(value) : 0;
        }

        private double? CalculateVelocityAngle(double? x, double? y)
        {
            if (!x.HasValue || !y.HasValue)
                return null;

            return Math.Atan2(y.Value, x.Value) * 180.0 / Math.PI;
        }

        private double? ReadDouble(string field, Func<object> read)
        {
            return SafeRead(field, () =>
            {
                double? value = ReflectionTools.AsDouble(read());
                if (!value.HasValue)
                    WarnMissing(field);
                return value;
            });
        }

        private bool? ReadBool(string field, Func<object> read)
        {
            return SafeRead(field, () =>
            {
                bool? value = ReflectionTools.AsBool(read());
                if (!value.HasValue)
                    WarnMissing(field);
                return value;
            });
        }

        private T SafeRead<T>(string field, Func<T> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                WarnMissing(field + " (" + ex.GetType().Name + ")");
                return default;
            }
        }

        private void WarnMissing(string field)
        {
            if (warnedMissingFields.Add(field))
                Debug.LogWarning("[FlightLogger] Missing field: " + field);
        }
    }
}
