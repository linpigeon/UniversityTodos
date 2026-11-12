using System;
using System.Threading.Tasks;
using UniversityTodos.Services;

namespace UniversityTodos.Desktop;

internal static class ElectricityCheck
{
    public static async Task<int> RunAsync()
    {
        var account = AppSettings.Current.ElectricityAccount;
        if (string.IsNullOrWhiteSpace(account))
        {
            Console.WriteLine("未保存完美校园学号");
            return 2;
        }

        var api = new ElectricityApiClient();
        try
        {
            var rooms = await api.GetBoundRoomsAsync(account);
            if (rooms is null)
            {
                Console.WriteLine($"FAIL: {api.LastError ?? "接口未返回有效数据"}");
                return 1;
            }
            Console.WriteLine($"PASS: 获取 {rooms.Count} 个宿舍；首个宿舍剩余 {rooms[0].Odd:F2} 度");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
