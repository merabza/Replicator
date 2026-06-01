using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using ParametersManagement.LibParameters;
using ReplicatorShared.Data.Models;
using SystemTools.BackgroundTasks;
using SystemTools.SystemToolsShared;

namespace Replicator;

public sealed class JobStarter
{
    private readonly string _appName;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly Dictionary<string, DateTime> _nextRunDatesByScheduleNames = [];
    private readonly ParametersLoader<ReplicatorParameters> _parLoader;
    private readonly IProcesses _processes;
    private readonly string _replicatorParametersFileName;
    private DateTime _nextJobDateTime;

    // ReSharper disable once ConvertToPrimaryConstructor
    public JobStarter(string appName, ILogger logger, IHttpClientFactory httpClientFactory, IProcesses processes,
        string replicatorParametersFileName)
    {
        _appName = appName;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _processes = processes;
        _replicatorParametersFileName = replicatorParametersFileName;
        _parLoader = new ParametersLoader<ReplicatorParameters>();
    }

    public void Run()
    {
        _logger.LogInformation("JobStarter.Run started");

        //მოვძებნოთ თუ არსებობს ისეთი დაგეგმვა,
        //რომელიც ითვალისწინებს სამუშაოს გაშვებას პროგრამის გაშვებისთანავე
        //თუ ასეთი დაგეგმვა მოიძებნა გაეშვას შესაბამისი სამუშაო ცალკე ნაკადში
        FindRunStartUpJobs(false);
    }

    private void FindRunStartUpJobs(bool byTime)
    {
        _parLoader.TryLoadParameters(_replicatorParametersFileName);
        var parameters = (ReplicatorParameters?)_parLoader.Par;
        if (parameters == null)
        {
            return;
        }

        Dictionary<string, JobSchedule> jobSchedulesDict =
            parameters.GetStartUpJobSchedules(byTime, _nextRunDatesByScheduleNames);

        string? procLogFilesFolder = parameters.CountLocalPath(parameters.ProcLogFilesFolder,
            _replicatorParametersFileName, "ProcLogFiles");

        if (string.IsNullOrWhiteSpace(procLogFilesFolder))
        {
            _logger.LogInformation("procLogFilesFolder does not specified");
            return;
        }

        if (!StShared.CreateFolder(procLogFilesFolder, false))
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("{ProcLogFilesFolder} does not exists and cannot be created",
                    procLogFilesFolder);
            }

            return;
        }

        foreach (KeyValuePair<string, JobSchedule> kvp in jobSchedulesDict)
        {
            //ეს მინიჭება საჭიროა იმისათვის რომ მერე მოხდეს ახალი დროის დაანგარიშება
            _nextJobDateTime = DateTime.MaxValue;

            parameters.RunAllSteps(_appName, _logger, _httpClientFactory, false, kvp.Key, _processes,
                procLogFilesFolder);
        }

        if (_nextJobDateTime == DateTime.MaxValue)
        {
            CalculateNextStartTime(parameters);
        }
    }

    private void CalculateNextStartTime(ReplicatorParameters parameters)
    {
        //ყველა არსებული დაგეგმვისათვის მოხდეს იმის დაანგარიშება, თუ როდის უწევს შემდეგი გაშვება
        //მათ შორის გამოვითვალოთ მინიმალური და დავიმახსოვროთ ცვლადში,
        //რომელიც გვაჩვენებს შემდეგი დათვლის გაშვების დროს
        //ეს ცვლადია nextJobDateTime
        var minNexStartTime = DateTime.MaxValue;

        //ამ პროცესის ჩატარების დროს აგრეთვე უნდა შევინახოთ ყველა დაგეგმვის შესაბამისი შემდეგი გაშვების დრო
        //0;AtStart;1;Once;2;Daily;3;weekly;4;monthly;5;WhenCPUIdle
        //IEnumerable<DsJobs.JobSchedulesRow> startUpJobSchedules = ProcData.Instance.GetNotStartUpJobSchedules();
        Dictionary<string, JobSchedule> jobSchedulesDict = parameters.GetNotStartUpJobSchedules();

        foreach (KeyValuePair<string, JobSchedule> kvp in jobSchedulesDict)
        {
            DateTime nextDateTime = RecountNextStartTime(kvp);

            _nextRunDatesByScheduleNames[kvp.Key] = nextDateTime;

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("NextStartTime for Job Schedule={Key} - {NextDateTime}", kvp.Key, nextDateTime);
            }

            if (nextDateTime < minNexStartTime)
            {
                minNexStartTime = nextDateTime;
            }
        }

        _nextJobDateTime = minNexStartTime;
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Minimum NextStartTime for all jobs is {MinNexStartTime}", minNexStartTime);
        }
    }

    private static DateTime RecountNextStartTime(KeyValuePair<string, JobSchedule> kvp)
    {
        JobSchedule jobSchedule = kvp.Value;

        //TimeSpan toRet = TimeSpan.MaxValue;
        //0;AtStart;1;Once;2;Daily;3;weekly;4;monthly;5;WhenCPUIdle

        DateTime firstDateTime = jobSchedule.DurationStartDate.Add(new TimeSpan(jobSchedule.ActiveStartDayTime.Hours,
            jobSchedule.ActiveStartDayTime.Minutes, jobSchedule.ActiveStartDayTime.Seconds));
        DateTime nowDateTime = DateTime.Now;

        switch (jobSchedule.ScheduleType)
        {
            case EScheduleType.Once:
                return firstDateTime < nowDateTime ? DateTime.MaxValue : firstDateTime;
            case EScheduleType.Daily:

                DateTime toDayStart = DateTime.Today.Add(jobSchedule.ActiveStartDayTime);
                DateTime toDayEnd = DateTime.Today.Add(jobSchedule.ActiveEndDayTime);

                int daysToAdd = (int)(nowDateTime.Date - firstDateTime.Date).TotalDays / jobSchedule.FreqInterval *
                                jobSchedule.FreqInterval;
                DateTime candidateStartDateTime = firstDateTime.AddDays(daysToAdd);
                DateTime nextCandidateStartDateTime = candidateStartDateTime;
                if (candidateStartDateTime < nowDateTime)
                {
                    nextCandidateStartDateTime = candidateStartDateTime.AddDays(jobSchedule.FreqInterval);
                }

                if (jobSchedule.DailyFrequencyType == EDailyFrequency.OccursOnce ||
                    candidateStartDateTime.Date != nowDateTime.Date || toDayEnd < nowDateTime)
                {
                    return nextCandidateStartDateTime.Date <= jobSchedule.DurationEndDate.Date
                        ? nextCandidateStartDateTime
                        : DateTime.MaxValue;
                }

                int atStartMinutes = toDayStart.Hour * 60 + toDayStart.Minute;
                int minuteInterval = jobSchedule.FreqSubDayInterval;
                if (jobSchedule.FreqSubDayType == EEveryMeasure.Hour)
                {
                    minuteInterval = jobSchedule.FreqSubDayInterval * 60;
                }

                //toRet = now.Date.AddHours(now.Hour).AddMinutes(now.Minute);
                int curMinutes = nowDateTime.Hour * 60 + nowDateTime.Minute;
                int minutesToAdd = (curMinutes - atStartMinutes) / minuteInterval * minuteInterval;
                DateTime candidateDateTime = toDayStart.AddMinutes(minutesToAdd);
                DateTime nextCandidateDateTime = candidateStartDateTime;
                if (candidateDateTime < nowDateTime)
                {
                    nextCandidateDateTime = candidateDateTime.AddMinutes(minuteInterval);
                }

                if (nextCandidateDateTime < toDayEnd && nextCandidateDateTime.Date <= jobSchedule.DurationEndDate.Date)
                {
                    return nextCandidateDateTime;
                }

                return nextCandidateStartDateTime.Date <= jobSchedule.DurationEndDate.Date
                    ? nextCandidateStartDateTime
                    : DateTime.MaxValue;
        }

        return DateTime.MaxValue;
    }

    public void DoTimerEventAnswer()
    {
        //დავადგინოთ მოვიდა თუ არა შემდეგი გადაანგარიშების დრო
        //ამისათვის ჩატვირთულ ინფორმაციაში უნდა მოვიძიოთ
        //რომელ სამუშაოს აქვს ის დრო რომელიც ეხლაა

        //ითვლება რომ ახალ სამუშაოს არ აინტერესებს ძველი დამთავრდა თუ არა
        //(თუ საჭირო გახდა ამის მართვა მერეც შეგვიძლია დავამატოთ)

        if (_nextJobDateTime <= DateTime.Now)
        {
            FindRunStartUpJobs(true);
        }
        else
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("next Job Date Time is: {_nextJobDateTime}, ", _nextJobDateTime);
            }
        }
    }
}
