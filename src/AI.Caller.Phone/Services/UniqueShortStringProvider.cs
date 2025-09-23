using Microsoft.Extensions.Options;

namespace AI.Caller.Phone.Services {
    public class UniqueShortStringProvider {
        static readonly object _lock = new object();
        static long prve = 0;
        static string Charactors { get; set; } = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ$";

        public UniqueShortStringProvider() { }

        public static string Create() {
            lock (_lock) {
                var startTime = new DateTime(2000, 1, 1, 0, 0, 0, 0);
                TimeSpan ts = DateTime.UtcNow - startTime;
                long temp = Convert.ToInt64(ts.TotalMilliseconds * 10);
                if (temp > prve) {
                    prve = temp;
                    return ToShortString(temp);
                } else {
                    prve++;
                    return ToShortString(prve);
                }
            }
        }

        private static string ToShortString(long num) {
            char[] sc = Charactors.ToCharArray();
            string str = "";
            while (num >= sc.Length) {
                str = sc[num % sc.Length] + str;
                num = num / sc.Length;
            }
            return sc[num] + str;
        }
    }
}