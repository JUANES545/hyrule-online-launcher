using System;

namespace HyruleOnline {
 public enum MotionStatus { Unavailable, Waiting, Active, Stale }

 public struct MotionReading {
  public MotionStatus Status;
  public bool Accelerometer, Gyroscope, AccelFresh, GyroFresh;
  public float Ax, Ay, Az, Gx, Gy, Gz;
  // Tilt in g relative to the pose when motion started, the gyro Z angle in degrees (clamped), and a 0..1 activity level.
  public double TiltX, TiltY, TiltZ, Angle, Activity;
 }

 // One tracker per SDL device. Timestamps are SDL nanoseconds.
 public sealed class ControllerMotion {
  public const ulong FreshNanoseconds = 500000000;
  const double Gravity = 9.80665;
  bool accel, gyro, hasAccel, hasGyro;
  ulong accelTime, gyroTime;
  float ax, ay, az, gx, gy, gz, baseX, baseY, baseZ;
  double angle;

  public void Configure(bool accelerometer, bool gyroscope) {
   accel = accelerometer; gyro = gyroscope; hasAccel = hasGyro = false; accelTime = gyroTime = 0;
   ax = ay = az = gx = gy = gz = baseX = baseY = baseZ = 0; angle = 0;
  }

  static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
  static double Clamp(double value, double limit) { return Math.Max(-limit, Math.Min(limit, value)); }
  static bool Fresh(ulong timestamp, ulong now) { return timestamp != 0 && timestamp <= now && now - timestamp <= FreshNanoseconds; }

  // Sensor 1 is the accelerometer (m/s^2), 2 the gyroscope (rad/s).
  public bool Accept(int sensor, float x, float y, float z, ulong timestamp, ulong now) {
   if (timestamp == 0 || timestamp > now || now - timestamp > FreshNanoseconds || !Finite(x) || !Finite(y) || !Finite(z)) return false;
   if (sensor == 1 && accel) {
    if (timestamp <= accelTime || Math.Abs(x) > 200 || Math.Abs(y) > 200 || Math.Abs(z) > 200 || x * x + y * y + z * z < 0.0001) return false;
    if (!hasAccel || !Fresh(accelTime, now)) { baseX = x; baseY = y; baseZ = z; angle = 0; }
    ax = x; ay = y; az = z; accelTime = timestamp; hasAccel = true; return true;
   }
   if (sensor == 2 && gyro) {
    if (timestamp <= gyroTime || Math.Abs(x) > 100 || Math.Abs(y) > 100 || Math.Abs(z) > 100) return false;
    double dt = hasGyro && Fresh(gyroTime, now) ? Math.Min(.05, (timestamp - gyroTime) / 1000000000.0) : 0;
    angle = Clamp(angle + z * dt * 180 / Math.PI, 35);
    gx = x; gy = y; gz = z; gyroTime = timestamp; hasGyro = true; return true;
   }
   return false;
  }

  public MotionReading Read(ulong now) {
   bool af = hasAccel && Fresh(accelTime, now), gf = hasGyro && Fresh(gyroTime, now);
   var result = new MotionReading {
    Accelerometer = accel, Gyroscope = gyro, AccelFresh = af, GyroFresh = gf,
    Ax = af ? ax : 0, Ay = af ? ay : 0, Az = af ? az : 0, Gx = gf ? gx : 0, Gy = gf ? gy : 0, Gz = gf ? gz : 0
   };
   result.Status = !accel && !gyro ? MotionStatus.Unavailable : af || gf ? MotionStatus.Active : hasAccel || hasGyro ? MotionStatus.Stale : MotionStatus.Waiting;
   if (af) { result.TiltX = Clamp((ax - baseX) / Gravity, 1); result.TiltY = Clamp((ay - baseY) / Gravity, 1); result.TiltZ = Clamp((az - baseZ) / Gravity, 1); }
   result.Angle = af || gf ? angle : 0;
   result.Activity = Math.Min(1, Math.Sqrt(result.Gx * result.Gx + result.Gy * result.Gy + result.Gz * result.Gz) / 2 + Math.Abs(result.TiltX) + Math.Abs(result.TiltY) + Math.Abs(result.TiltZ));
   return result;
  }
 }
}
