using System;
using Microsoft.Extensions.DependencyInjection;
using Replicator.HostedServices;
using SystemTools.BackgroundTasks;

namespace Replicator.DependencyInjection;

// ReSharper disable once UnusedType.Global
public static class HostedServiceDependencyInjection
{
    public static IServiceCollection AddHostedServices(this IServiceCollection services, bool debugMode)
    {
        if (debugMode)
        {
            Console.WriteLine($"{nameof(AddHostedServices)} Started");
        }

        //სერვისი, რომლის დანიშნულებაცაა რიგში ჩააყენოს და გააკონტროლოს პროცესები, რომლებიც უნდა შესრულდეს
        services.AddSingleton<IProcesses, Processes>();

        //სერვისი, რომელიც პერიოდულად შეამოწმებს პროცესების რიგს და შეასრულებს მათ,
        //აგრეთვე აკონტროლებს აპლიკაციის სიცოცხლის ციკლს და აპლიკაციის დახურვისას პროცესების რიგში ჩაყენებულ პროცესებს გააუქმებს
        services.AddHostedService<TimedHostedService>();

        if (debugMode)
        {
            Console.WriteLine($"{nameof(AddHostedServices)} Finished");
        }

        return services;
    }
}
