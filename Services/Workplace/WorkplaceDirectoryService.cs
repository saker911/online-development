using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Workplace;
using VehiclePermitSystemWeb.Services.Common;

namespace VehiclePermitSystemWeb.Services.Workplace
{
    public sealed class WorkplaceDirectoryService : IWorkplaceDirectoryService
    {
        private const int MaximumPageSize = 50;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemClock _systemClock;

        public WorkplaceDirectoryService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemClock systemClock
        )
        {
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
        }

        public PeopleDirectoryViewModel BuildPeopleDirectory(
            string? query,
            string? personType,
            int page,
            int pageSize
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizeOperationalDirectory(db);

            var normalizedQuery = (query ?? string.Empty).Trim();
            var normalizedType = NormalizePersonType(personType);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 8, MaximumPageSize);

            var profiles = db.PersonProfiles.AsNoTracking().ToList();
            var photoProfileIds = db.PersonPhotos.AsNoTracking()
                .Select(photo => photo.PersonProfileId)
                .ToHashSet();
            var presence = BuildPresenceItems(db);
            var presenceByReference = presence
                .GroupBy(item => item.Reference, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var items = profiles
                .Select(profile => MapDirectoryItem(
                    profile,
                    presenceByReference,
                    photoProfileIds.Contains(profile.Id)
                ))
                .Where(item =>
                    string.IsNullOrWhiteSpace(normalizedType)
                    || string.Equals(item.PersonType, normalizedType, StringComparison.OrdinalIgnoreCase)
                )
                .Where(item =>
                    string.IsNullOrWhiteSpace(normalizedQuery)
                    || Contains(item.FullName, normalizedQuery)
                    || Contains(item.Reference, normalizedQuery)
                    || Contains(item.Department, normalizedQuery)
                    || Contains(item.JobTitle, normalizedQuery)
                )
                .OrderByDescending(item => item.IsOnSite)
                .ThenByDescending(item => item.LastActivityAt)
                .ThenBy(item => item.FullName)
                .ToList();

            var totalCount = items.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Min(page, totalPages);

            return new PeopleDirectoryViewModel
            {
                Query = normalizedQuery,
                PersonType = normalizedType,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                EmployeeCount = profiles.Count(profile => profile.PersonType == PersonTypes.Employee),
                VisitorCount = profiles.Count(profile => profile.PersonType == PersonTypes.Visitor),
                ContractorCount = profiles.Count(profile => profile.PersonType == PersonTypes.Contractor),
                OnSiteCount = presence.Count,
                People = items.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            };
        }

        public PersonDetailsViewModel? BuildPersonDetails(long id)
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizeOperationalDirectory(db);

            var profile = db.PersonProfiles.AsNoTracking().FirstOrDefault(item => item.Id == id);
            if (profile == null)
            {
                return null;
            }

            var permitsQuery = db.Permits.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(profile.EmployeeNumber))
            {
                permitsQuery = permitsQuery.Where(permit =>
                    permit.EmployeeNumber == profile.EmployeeNumber
                );
            }
            else if (!string.IsNullOrWhiteSpace(profile.NationalId))
            {
                permitsQuery = permitsQuery.Where(permit => permit.NationalId == profile.NationalId);
            }
            else if (!string.IsNullOrWhiteSpace(profile.PhoneNumber))
            {
                permitsQuery = permitsQuery.Where(permit =>
                    permit.EmployeePhone == profile.PhoneNumber
                );
            }
            else
            {
                permitsQuery = permitsQuery.Where(permit =>
                    permit.PermitNumber == profile.LastReference
                );
            }

            var visitsQuery = db.Visits.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(profile.NationalId))
            {
                visitsQuery = visitsQuery.Where(visit => visit.NationalId == profile.NationalId);
            }
            else if (!string.IsNullOrWhiteSpace(profile.PhoneNumber))
            {
                visitsQuery = visitsQuery.Where(visit => visit.PhoneNumber == profile.PhoneNumber);
            }
            else
            {
                visitsQuery = visitsQuery.Where(visit => visit.VisitId == profile.LastReference);
            }

            var permits = profile.PersonType == PersonTypes.Visitor
                ? []
                : permitsQuery.OrderByDescending(permit => permit.PermitDate)
                    .ThenByDescending(permit => permit.ExpiresAt)
                    .Take(30)
                    .ToList();
            var visits = profile.PersonType == PersonTypes.Visitor
                ? visitsQuery.OrderByDescending(visit => visit.VisitDate).Take(30).ToList()
                : [];

            var permitNumbers = permits.Select(permit => permit.PermitNumber).ToList();
            var permitActivities = permitNumbers.Count == 0
                ? []
                : db.PermitActivities.AsNoTracking()
                    .Where(activity => permitNumbers.Contains(activity.PermitNumber))
                    .OrderByDescending(activity => activity.OccurredAt)
                    .Take(80)
                    .ToList();

            var records = permits.Select(permit => new PersonRecordItemViewModel
                {
                    RecordType = "Permit",
                    RecordTypeDisplay = "تصريح",
                    Reference = permit.PermitNumber,
                    Title = FirstNotEmpty(permit.PlateNumber, permit.VehicleType, permit.Subject),
                    Status = permit.ApprovalStatusDisplay,
                    Location = FirstNotEmpty(permit.VisitLocation, permit.EmployeeDepartment, permit.DepartmentName),
                    OccurredAt = permit.PermitDate ?? permit.ExpiresAt,
                })
                .Concat(visits.Select(visit => new PersonRecordItemViewModel
                {
                    RecordType = "Visit",
                    RecordTypeDisplay = "زيارة",
                    Reference = visit.VisitId,
                    Title = FirstNotEmpty(visit.Purpose, visit.VisitedPersonName),
                    Status = $"{visit.ApprovalStatusDisplay} · {visit.StatusDisplay}",
                    Location = visit.VisitLocation,
                    OccurredAt = visit.VisitDate,
                }))
                .OrderByDescending(record => record.OccurredAt)
                .ToList();

            var timeline = permitActivities.Select(activity => new WorkplaceActivityItemViewModel
                {
                    Reference = activity.PermitNumber,
                    FullName = profile.FullName,
                    Action = FirstNotEmpty(activity.ActionLabel, activity.ActionType, "حركة تصريح"),
                    Location = FirstNotEmpty(activity.GateName, activity.Source),
                    OccurredAt = activity.OccurredAt,
                    IsEntry = activity.ActionType == "Entry" || activity.ActionType == "Return",
                })
                .Concat(BuildVisitTimeline(visits, profile.FullName))
                .OrderByDescending(item => item.OccurredAt)
                .Take(60)
                .ToList();

            var linkedReferences = records.Select(record => record.Reference)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var presence = BuildPresenceItems(db)
                .Where(item => linkedReferences.Contains(item.Reference))
                .OrderByDescending(item => item.EnteredAt)
                .FirstOrDefault();

            return new PersonDetailsViewModel
            {
                Id = profile.Id,
                FullName = profile.FullName,
                PersonType = profile.PersonType,
                PersonTypeDisplay = PersonTypes.DisplayName(profile.PersonType),
                Reference = profile.LastReference,
                NationalIdMasked = MaskIdentifier(profile.NationalId),
                PhoneNumberMasked = MaskPhone(profile.PhoneNumber),
                EmailMasked = MaskEmail(profile.Email),
                EmployeeNumber = profile.EmployeeNumber,
                Department = profile.Department,
                JobTitle = profile.JobTitle,
                Organization = profile.Organization,
                IsActive = profile.IsActive,
                HasPhoto = db.PersonPhotos.AsNoTracking().Any(photo =>
                    photo.PersonProfileId == profile.Id
                ),
                IsOnSite = presence != null,
                CurrentLocation = presence?.Location ?? string.Empty,
                CurrentPresenceSince = presence?.EnteredAt,
                LastActivityAt = timeline.Select(item => (DateTime?)item.OccurredAt)
                    .FirstOrDefault() ?? profile.LastSeenAtUtc,
                Records = records,
                Timeline = timeline,
            };
        }

        public KnownVisitorPrefillViewModel? BuildKnownVisitorPrefill(long id)
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizeOperationalDirectory(db);
            return db.PersonProfiles.AsNoTracking()
                .Where(profile =>
                    profile.Id == id
                    && profile.IsActive
                    && profile.PersonType == PersonTypes.Visitor
                )
                .Select(profile => new KnownVisitorPrefillViewModel
                {
                    PersonId = profile.Id,
                    FullName = profile.FullName,
                    NationalId = profile.NationalId,
                    PhoneNumber = profile.PhoneNumber,
                    Email = profile.Email,
                })
                .FirstOrDefault();
        }

        public PersonPhotoViewModel? GetPersonPhoto(long id)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return db.PersonPhotos.AsNoTracking()
                .Where(photo => photo.PersonProfileId == id)
                .Select(photo => new PersonPhotoViewModel
                {
                    Data = photo.Data,
                    ContentType = photo.ContentType,
                })
                .FirstOrDefault();
        }

        public bool SavePersonPhoto(
            long id,
            byte[] data,
            string contentType,
            out string error
        )
        {
            error = string.Empty;
            if (data.Length == 0 || string.IsNullOrWhiteSpace(contentType))
            {
                error = "بيانات الصورة غير صالحة.";
                return false;
            }

            using var db = _dbContextFactory.CreateDbContext();
            if (!db.PersonProfiles.AsNoTracking().Any(profile => profile.Id == id))
            {
                error = "ملف الشخص غير موجود.";
                return false;
            }

            var photo = db.PersonPhotos.FirstOrDefault(item => item.PersonProfileId == id);
            if (photo == null)
            {
                photo = new PersonPhoto { PersonProfileId = id };
                db.PersonPhotos.Add(photo);
            }

            photo.Data = data;
            photo.ContentType = contentType.Trim().ToLowerInvariant();
            photo.UpdatedAtUtc = _systemClock.UtcNow;
            db.SaveChanges();
            return true;
        }

        public bool DeletePersonPhoto(long id, out string error)
        {
            error = string.Empty;
            using var db = _dbContextFactory.CreateDbContext();
            var photo = db.PersonPhotos.FirstOrDefault(item => item.PersonProfileId == id);
            if (photo == null)
            {
                error = "لا توجد صورة محفوظة لهذا الشخص.";
                return false;
            }

            db.PersonPhotos.Remove(photo);
            db.SaveChanges();
            return true;
        }

        public AttendanceDashboardViewModel BuildAttendanceDashboard(
            string? query,
            string? personType
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizeOperationalDirectory(db);

            var normalizedQuery = (query ?? string.Empty).Trim();
            var normalizedType = NormalizePersonType(personType);
            var allPresent = BuildPresenceItems(db);
            var present = allPresent
                .Where(item =>
                    string.IsNullOrWhiteSpace(normalizedType)
                    || string.Equals(item.PersonType, normalizedType, StringComparison.OrdinalIgnoreCase)
                )
                .Where(item =>
                    string.IsNullOrWhiteSpace(normalizedQuery)
                    || Contains(item.FullName, normalizedQuery)
                    || Contains(item.Reference, normalizedQuery)
                    || Contains(item.Location, normalizedQuery)
                    || Contains(item.Destination, normalizedQuery)
                )
                .OrderByDescending(item => item.EnteredAt)
                .ToList();

            return new AttendanceDashboardViewModel
            {
                Query = normalizedQuery,
                PersonType = normalizedType,
                TotalOnSite = allPresent.Count,
                EmployeesOnSite = allPresent.Count(item => item.PersonType == PersonTypes.Employee),
                VisitorsOnSite = allPresent.Count(item => item.PersonType == PersonTypes.Visitor),
                ContractorsOnSite = allPresent.Count(item => item.PersonType == PersonTypes.Contractor),
                ActiveSiteCount = db.WorkplaceSites.Count(site => site.IsActive),
                UpdatedAt = _systemClock.LocalNow,
                PresentPeople = present,
                RecentActivity = BuildRecentActivity(db),
            };
        }

        public WorkplaceSitesViewModel BuildSites(int? editId = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizeOperationalDirectory(db);

            var present = BuildPresenceItems(db);
            var sites = db.WorkplaceSites.AsNoTracking().OrderByDescending(site => site.IsActive)
                .ThenBy(site => site.Name).ToList();
            var entranceCounts = db.WorkplaceSiteEntrances.AsNoTracking()
                .GroupBy(entrance => entrance.WorkplaceSiteId)
                .ToDictionary(group => group.Key, group => group.Count());
            var deviceCounts = db.DisplayDevices.AsNoTracking()
                .Where(device => device.WorkplaceSiteId.HasValue)
                .GroupBy(device => device.WorkplaceSiteId!.Value)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        Total = group.Count(),
                        Online = group.Count(device =>
                            device.Status == DisplayDeviceStatuses.Approved
                            && device.LastSeenUtc.HasValue
                            && _systemClock.UtcNow - device.LastSeenUtc.Value <= TimeSpan.FromMinutes(2)
                        ),
                    }
                );
            var editing = editId.HasValue ? sites.FirstOrDefault(site => site.Id == editId.Value) : null;

            return new WorkplaceSitesViewModel
            {
                Input = editing == null
                    ? new WorkplaceSiteInputViewModel()
                    : new WorkplaceSiteInputViewModel
                    {
                        Id = editing.Id,
                        Name = editing.Name,
                        Code = editing.Code,
                        Address = editing.Address,
                        Latitude = editing.Latitude,
                        Longitude = editing.Longitude,
                        GeofenceRadiusMeters = editing.GeofenceRadiusMeters,
                        PermitsEnabled = editing.PermitsEnabled,
                        VisitsEnabled = editing.VisitsEnabled,
                        SelfServiceEnabled = editing.SelfServiceEnabled,
                        QueueEnabled = editing.QueueEnabled,
                        GateEnabled = editing.GateEnabled,
                        IsActive = editing.IsActive,
                    },
                Sites = sites.Select(site => new WorkplaceSiteItemViewModel
                    {
                        Id = site.Id,
                        Name = site.Name,
                        Code = site.Code,
                        Address = site.Address,
                        GeofenceRadiusMeters = site.GeofenceRadiusMeters,
                        IsActive = site.IsActive,
                        CurrentPeopleCount = present.Count(item =>
                            string.Equals(item.Location, site.Name, StringComparison.OrdinalIgnoreCase)
                        ),
                        EntranceCount = entranceCounts.GetValueOrDefault(site.Id),
                        DeviceCount = deviceCounts.TryGetValue(site.Id, out var count)
                            ? count.Total
                            : 0,
                        OnlineDeviceCount = deviceCounts.TryGetValue(site.Id, out count)
                            ? count.Online
                            : 0,
                        PermitsEnabled = site.PermitsEnabled,
                        VisitsEnabled = site.VisitsEnabled,
                        SelfServiceEnabled = site.SelfServiceEnabled,
                        QueueEnabled = site.QueueEnabled,
                        GateEnabled = site.GateEnabled,
                    })
                    .ToList(),
            };
        }

        public WorkplaceSiteDetailsViewModel? BuildSiteDetails(int id)
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizeOperationalDirectory(db);

            var site = db.WorkplaceSites.AsNoTracking().FirstOrDefault(item => item.Id == id);
            if (site == null)
            {
                return null;
            }

            var entrances = db.WorkplaceSiteEntrances.AsNoTracking()
                .Where(item => item.WorkplaceSiteId == id)
                .OrderByDescending(item => item.IsActive)
                .ThenBy(item => item.Name)
                .ToList();
            var devices = db.DisplayDevices.AsNoTracking()
                .Where(item => item.WorkplaceSiteId == id)
                .OrderBy(item => item.ScreenName)
                .ToList();
            var availableDevices = db.DisplayDevices.AsNoTracking()
                .Where(item => item.WorkplaceSiteId == null)
                .OrderByDescending(item => item.Status == DisplayDeviceStatuses.Approved)
                .ThenBy(item => item.ScreenName)
                .ToList();
            var entranceNames = entrances.ToDictionary(item => item.Id, item => item.Name);
            var now = _systemClock.UtcNow;
            var currentPeopleCount = BuildPresenceItems(db).Count(item =>
                string.Equals(item.Location, site.Name, StringComparison.OrdinalIgnoreCase)
            );

            return new WorkplaceSiteDetailsViewModel
            {
                Site = new WorkplaceSiteItemViewModel
                {
                    Id = site.Id,
                    Name = site.Name,
                    Code = site.Code,
                    Address = site.Address,
                    GeofenceRadiusMeters = site.GeofenceRadiusMeters,
                    IsActive = site.IsActive,
                    CurrentPeopleCount = currentPeopleCount,
                    EntranceCount = entrances.Count,
                    DeviceCount = devices.Count,
                    OnlineDeviceCount = devices.Count(device => IsDeviceOnline(device, now)),
                    PermitsEnabled = site.PermitsEnabled,
                    VisitsEnabled = site.VisitsEnabled,
                    SelfServiceEnabled = site.SelfServiceEnabled,
                    QueueEnabled = site.QueueEnabled,
                    GateEnabled = site.GateEnabled,
                },
                EntranceInput = new WorkplaceSiteEntranceInputViewModel
                {
                    WorkplaceSiteId = site.Id,
                },
                DeviceAssignment = new WorkplaceSiteDeviceAssignmentViewModel
                {
                    WorkplaceSiteId = site.Id,
                },
                Entrances = entrances.Select(entrance => new WorkplaceSiteEntranceItemViewModel
                    {
                        Id = entrance.Id,
                        Name = entrance.Name,
                        Code = entrance.Code,
                        LocationDescription = entrance.LocationDescription,
                        IsActive = entrance.IsActive,
                        DeviceCount = devices.Count(device =>
                            device.WorkplaceSiteEntranceId == entrance.Id
                        ),
                    })
                    .ToList(),
                Devices = devices.Select(device => new WorkplaceSiteDeviceItemViewModel
                    {
                        Id = device.Id,
                        Name = device.ScreenName,
                        Mode = DisplayDeviceModes.Normalize(device.Mode),
                        ModeDisplay = DisplayDeviceModes.GetDisplayName(device.Mode),
                        EntranceName = device.WorkplaceSiteEntranceId.HasValue
                            ? entranceNames.GetValueOrDefault(device.WorkplaceSiteEntranceId.Value)
                                ?? "غير محدد"
                            : "غير محدد",
                        Status = device.Status,
                        StatusDisplay = GetDeviceStatusDisplay(device, now),
                        IsOnline = IsDeviceOnline(device, now),
                        LastSeenUtc = device.LastSeenUtc,
                    })
                    .ToList(),
                AvailableDevices = availableDevices.Select(device =>
                    new WorkplaceUnassignedDeviceItemViewModel
                    {
                        Id = device.Id,
                        Name = device.ScreenName,
                        Location = device.ScreenLocation,
                        ModeDisplay = DisplayDeviceModes.GetDisplayName(device.Mode),
                    }).ToList(),
            };
        }

        public bool SaveSite(WorkplaceSiteInputViewModel model, out string error)
        {
            error = string.Empty;
            using var db = _dbContextFactory.CreateDbContext();
            var name = (model.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "اسم الموقع مطلوب.";
                return false;
            }

            var duplicate = db.WorkplaceSites.Any(site =>
                site.Id != (model.Id ?? 0) && site.Name.ToLower() == name.ToLower()
            );
            if (duplicate)
            {
                error = "يوجد موقع مسجل بالاسم نفسه.";
                return false;
            }

            WorkplaceSite? template = null;
            if (!model.Id.HasValue && model.CopyFromSiteId.HasValue)
            {
                template = db.WorkplaceSites.AsNoTracking()
                    .FirstOrDefault(item => item.Id == model.CopyFromSiteId.Value);
                if (template == null)
                {
                    error = "قالب الموقع المطلوب غير موجود.";
                    return false;
                }
            }

            var site = model.Id.HasValue
                ? db.WorkplaceSites.FirstOrDefault(item => item.Id == model.Id.Value)
                : new WorkplaceSite { CreatedAtUtc = DateTime.UtcNow };
            if (site == null)
            {
                error = "الموقع المطلوب غير موجود.";
                return false;
            }

            site.Name = name;
            site.Code = string.IsNullOrWhiteSpace(model.Code)
                ? BuildSiteCode(name, model.Id)
                : model.Code.Trim().ToUpperInvariant();
            site.Address = (model.Address ?? string.Empty).Trim();
            site.Latitude = model.Latitude;
            site.Longitude = model.Longitude;
            site.GeofenceRadiusMeters = Math.Clamp(model.GeofenceRadiusMeters, 25, 5000);
            site.PermitsEnabled = template?.PermitsEnabled ?? model.PermitsEnabled;
            site.VisitsEnabled = template?.VisitsEnabled ?? model.VisitsEnabled;
            site.SelfServiceEnabled = template?.SelfServiceEnabled ?? model.SelfServiceEnabled;
            site.QueueEnabled = template?.QueueEnabled ?? model.QueueEnabled;
            site.GateEnabled = template?.GateEnabled ?? model.GateEnabled;
            site.IsActive = model.IsActive;
            site.UpdatedAtUtc = DateTime.UtcNow;

            if (!model.Id.HasValue)
            {
                db.WorkplaceSites.Add(site);
            }

            db.SaveChanges();

            if (template != null)
            {
                var templateEntrances = db.WorkplaceSiteEntrances.AsNoTracking()
                    .Where(item => item.WorkplaceSiteId == template.Id)
                    .ToList();
                foreach (var entrance in templateEntrances)
                {
                    db.WorkplaceSiteEntrances.Add(new WorkplaceSiteEntrance
                    {
                        WorkplaceSiteId = site.Id,
                        Name = entrance.Name,
                        Code = entrance.Code,
                        LocationDescription = entrance.LocationDescription,
                        IsActive = entrance.IsActive,
                        CreatedAtUtc = _systemClock.UtcNow,
                        UpdatedAtUtc = _systemClock.UtcNow,
                    });
                }
                db.SaveChanges();
            }
            return true;
        }

        public IReadOnlyList<WorkplaceLocationOptionViewModel> GetLocationOptions()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var entrances = db.WorkplaceSiteEntrances.AsNoTracking()
                .Where(item => item.IsActive)
                .OrderBy(item => item.Name)
                .ToList();
            return db.WorkplaceSites.AsNoTracking()
                .Where(item => item.IsActive)
                .OrderBy(item => item.Name)
                .ToList()
                .Select(site => new WorkplaceLocationOptionViewModel
                {
                    SiteId = site.Id,
                    SiteName = site.Name,
                    PermitsEnabled = site.PermitsEnabled,
                    VisitsEnabled = site.VisitsEnabled,
                    SelfServiceEnabled = site.SelfServiceEnabled,
                    QueueEnabled = site.QueueEnabled,
                    GateEnabled = site.GateEnabled,
                    Entrances = entrances.Where(item => item.WorkplaceSiteId == site.Id)
                        .Select(item => new WorkplaceEntranceOptionViewModel
                        {
                            Id = item.Id,
                            Name = item.Name,
                        }).ToList(),
                }).ToList();
        }

        public bool ResolveLocation(
            int? siteId,
            int? entranceId,
            string service,
            out string siteName,
            out string entranceName,
            out string error
        )
        {
            siteName = string.Empty;
            entranceName = string.Empty;
            error = string.Empty;
            if (!siteId.HasValue)
            {
                error = "اختر الموقع التشغيلي.";
                return false;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var site = db.WorkplaceSites.AsNoTracking()
                .FirstOrDefault(item => item.Id == siteId.Value && item.IsActive);
            if (site == null)
            {
                error = "الموقع المحدد غير موجود أو غير نشط.";
                return false;
            }

            var enabled = service switch
            {
                "permits" => site.PermitsEnabled,
                "visits" => site.VisitsEnabled,
                "self-service" => site.SelfServiceEnabled,
                "self-service-visits" => site.SelfServiceEnabled && site.VisitsEnabled,
                "queue" => site.QueueEnabled,
                "gate" => site.GateEnabled,
                _ => true,
            };
            if (!enabled)
            {
                error = "الخدمة المطلوبة غير مفعلة في هذا الموقع.";
                return false;
            }

            siteName = site.Name;
            if (!entranceId.HasValue)
            {
                return true;
            }

            var entrance = db.WorkplaceSiteEntrances.AsNoTracking()
                .FirstOrDefault(item => item.Id == entranceId.Value
                    && item.WorkplaceSiteId == site.Id && item.IsActive);
            if (entrance == null)
            {
                error = "المدخل المحدد لا يتبع الموقع أو غير نشط.";
                return false;
            }

            entranceName = entrance.Name;
            return true;
        }

        public bool ToggleSite(int id, out string error)
        {
            error = string.Empty;
            using var db = _dbContextFactory.CreateDbContext();
            var site = db.WorkplaceSites.FirstOrDefault(item => item.Id == id);
            if (site == null)
            {
                error = "الموقع المطلوب غير موجود.";
                return false;
            }

            site.IsActive = !site.IsActive;
            site.UpdatedAtUtc = DateTime.UtcNow;
            db.SaveChanges();
            return true;
        }

        public bool DeleteSite(int id, out string error)
        {
            error = string.Empty;
            using var db = _dbContextFactory.CreateDbContext();
            var site = db.WorkplaceSites.FirstOrDefault(item => item.Id == id);
            if (site == null)
            {
                error = "الموقع المطلوب غير موجود.";
                return false;
            }

            if (db.DisplayDevices.Any(device => device.WorkplaceSiteId == id))
            {
                error = "لا يمكن حذف موقع مرتبط بأجهزة. أزل إسناد الأجهزة أولاً.";
                return false;
            }

            if (db.Permits.Any(item => item.WorkplaceSiteId == id)
                || db.Visits.Any(item => item.WorkplaceSiteId == id)
                || db.EmergencySessions.Any(item => item.WorkplaceSiteId == id))
            {
                error = "لا يمكن حذف موقع مرتبط بطلبات تشغيلية. عطّل الموقع بدلاً من حذفه.";
                return false;
            }

            db.WorkplaceSites.Remove(site);
            db.SaveChanges();
            return true;
        }

        public bool SaveEntrance(WorkplaceSiteEntranceInputViewModel model, out string error)
        {
            error = string.Empty;
            using var db = _dbContextFactory.CreateDbContext();
            if (!db.WorkplaceSites.Any(site => site.Id == model.WorkplaceSiteId))
            {
                error = "الموقع المطلوب غير موجود.";
                return false;
            }

            var name = (model.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "اسم المدخل مطلوب.";
                return false;
            }

            if (db.WorkplaceSiteEntrances.Any(entrance =>
                entrance.WorkplaceSiteId == model.WorkplaceSiteId
                && entrance.Name.ToLower() == name.ToLower()
            ))
            {
                error = "يوجد مدخل بالاسم نفسه في هذا الموقع.";
                return false;
            }

            var entrance = new WorkplaceSiteEntrance
            {
                WorkplaceSiteId = model.WorkplaceSiteId,
                Name = name,
                Code = string.IsNullOrWhiteSpace(model.Code)
                    ? BuildEntranceCode(name)
                    : model.Code.Trim().ToUpperInvariant(),
                LocationDescription = (model.LocationDescription ?? string.Empty).Trim(),
                IsActive = true,
                CreatedAtUtc = _systemClock.UtcNow,
                UpdatedAtUtc = _systemClock.UtcNow,
            };
            db.WorkplaceSiteEntrances.Add(entrance);
            db.SaveChanges();
            return true;
        }

        public bool ToggleEntrance(int siteId, int entranceId, out string error)
        {
            error = string.Empty;
            using var db = _dbContextFactory.CreateDbContext();
            var entrance = db.WorkplaceSiteEntrances.FirstOrDefault(item =>
                item.Id == entranceId && item.WorkplaceSiteId == siteId
            );
            if (entrance == null)
            {
                error = "المدخل المطلوب غير موجود.";
                return false;
            }

            entrance.IsActive = !entrance.IsActive;
            entrance.UpdatedAtUtc = _systemClock.UtcNow;
            db.SaveChanges();
            return true;
        }

        public bool DeleteEntrance(int siteId, int entranceId, out string error)
        {
            error = string.Empty;
            using var db = _dbContextFactory.CreateDbContext();
            var entrance = db.WorkplaceSiteEntrances.FirstOrDefault(item =>
                item.Id == entranceId && item.WorkplaceSiteId == siteId
            );
            if (entrance == null)
            {
                error = "المدخل المطلوب غير موجود.";
                return false;
            }

            if (db.DisplayDevices.Any(device => device.WorkplaceSiteEntranceId == entranceId))
            {
                error = "لا يمكن حذف مدخل مرتبط بجهاز. غيّر إسناد الجهاز أولاً.";
                return false;
            }

            if (db.Permits.Any(item => item.WorkplaceSiteEntranceId == entranceId)
                || db.Visits.Any(item => item.WorkplaceSiteEntranceId == entranceId))
            {
                error = "لا يمكن حذف مدخل مرتبط بطلبات تشغيلية. عطّل المدخل بدلاً من حذفه.";
                return false;
            }

            db.WorkplaceSiteEntrances.Remove(entrance);
            db.SaveChanges();
            return true;
        }

        public bool AssignDevice(WorkplaceSiteDeviceAssignmentViewModel model, out string error)
        {
            error = string.Empty;
            using var db = _dbContextFactory.CreateDbContext();
            var site = db.WorkplaceSites.AsNoTracking()
                .FirstOrDefault(item => item.Id == model.WorkplaceSiteId);
            var device = db.DisplayDevices.FirstOrDefault(item => item.Id == model.DisplayDeviceId);
            if (site == null || device == null)
            {
                error = "الموقع أو الجهاز المطلوب غير موجود.";
                return false;
            }

            WorkplaceSiteEntrance? entrance = null;
            if (model.WorkplaceSiteEntranceId.HasValue)
            {
                entrance = db.WorkplaceSiteEntrances.AsNoTracking().FirstOrDefault(item =>
                    item.Id == model.WorkplaceSiteEntranceId.Value
                    && item.WorkplaceSiteId == model.WorkplaceSiteId
                    && item.IsActive
                );
                if (entrance == null)
                {
                    error = "المدخل المختار غير تابع للموقع أو غير نشط.";
                    return false;
                }
            }

            device.WorkplaceSiteId = site.Id;
            device.WorkplaceSiteEntranceId = entrance?.Id;
            device.ScreenLocation = entrance?.Name ?? site.Name;
            device.ConfigurationVersion++;
            db.SaveChanges();
            return true;
        }

        public bool UnassignDevice(int siteId, int deviceId, out string error)
        {
            error = string.Empty;
            using var db = _dbContextFactory.CreateDbContext();
            var device = db.DisplayDevices.FirstOrDefault(item =>
                item.Id == deviceId && item.WorkplaceSiteId == siteId
            );
            if (device == null)
            {
                error = "الجهاز غير مرتبط بهذا الموقع.";
                return false;
            }

            device.WorkplaceSiteId = null;
            device.WorkplaceSiteEntranceId = null;
            device.ConfigurationVersion++;
            db.SaveChanges();
            return true;
        }

        private static void SynchronizeOperationalDirectory(ApplicationDbContext db)
        {
            var now = DateTime.UtcNow;
            var existing = db.PersonProfiles.ToDictionary(
                profile => profile.SourceKey,
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var user in db.UserAccounts.AsNoTracking())
            {
                var key = BuildEmployeeSourceKey(
                    user.EmployeeNumber,
                    string.Empty,
                    user.PhoneNumber,
                    user.Username
                );
                UpsertProfile(
                    db,
                    existing,
                    key,
                    PersonTypes.Employee,
                    FirstNotEmpty(user.FullName, user.DisplayName, user.Username),
                    string.Empty,
                    user.PhoneNumber,
                    user.Email,
                    user.EmployeeNumber,
                    user.Department,
                    user.JobTitle,
                    string.Empty,
                    user.Username,
                    user.IsActive,
                    null,
                    now
                );
            }

            var latestPermitActivity = db.PermitActivities.AsNoTracking()
                .GroupBy(activity => activity.PermitNumber)
                .Select(group => new
                {
                    PermitNumber = group.Key,
                    LastSeenAt = group.Max(activity => activity.OccurredAt),
                })
                .ToDictionary(item => item.PermitNumber, item => (DateTime?)item.LastSeenAt);

            foreach (var permit in db.Permits.AsNoTracking()
                .Where(permit => permit.ArchivedAt == null)
                .OrderBy(permit => permit.PermitDate))
            {
                var type = permit.IsPermanentPermit ? PersonTypes.Employee : PersonTypes.Contractor;
                var key = permit.IsPermanentPermit
                    ? BuildEmployeeSourceKey(
                        permit.EmployeeNumber,
                        permit.NationalId,
                        permit.EmployeePhone,
                        permit.PermitNumber
                    )
                    : BuildExternalSourceKey(type, permit.NationalId, permit.EmployeePhone, permit.PermitNumber);
                latestPermitActivity.TryGetValue(permit.PermitNumber, out var lastSeenAt);
                UpsertProfile(
                    db,
                    existing,
                    key,
                    type,
                    permit.DriverName,
                    permit.NationalId,
                    permit.EmployeePhone,
                    permit.HolderEmail ?? string.Empty,
                    permit.EmployeeNumber,
                    FirstNotEmpty(permit.EmployeeDepartment, permit.DepartmentName),
                    permit.JobTitle,
                    permit.AuthorizingEntity,
                    permit.PermitNumber,
                    !string.Equals(permit.ApprovalStatus, "Stopped", StringComparison.OrdinalIgnoreCase),
                    lastSeenAt,
                    now
                );
            }

            foreach (var visit in db.Visits.AsNoTracking()
                .Where(visit => visit.ArchivedAt == null)
                .OrderBy(visit => visit.VisitDate))
            {
                var key = BuildExternalSourceKey(
                    PersonTypes.Visitor,
                    visit.NationalId,
                    visit.PhoneNumber,
                    visit.VisitId
                );
                UpsertProfile(
                    db,
                    existing,
                    key,
                    PersonTypes.Visitor,
                    visit.VisitorName,
                    visit.NationalId,
                    visit.PhoneNumber,
                    visit.VisitorEmail ?? string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    visit.VisitId,
                    !string.Equals(visit.Status, "Suspended", StringComparison.OrdinalIgnoreCase),
                    visit.ExitTime ?? visit.EntryTime ?? visit.VisitDate,
                    now
                );
            }

            SynchronizeSites(db, now);
            db.SaveChanges();
        }

        private static void SynchronizeSites(ApplicationDbContext db, DateTime now)
        {
            var locationNames = db.Visits.AsNoTracking().Select(visit => visit.VisitLocation)
                .Concat(db.DisplayDevices.AsNoTracking().Select(device => device.ScreenLocation))
                .AsEnumerable()
                .Select(name => (name ?? string.Empty).Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existingNames = db.WorkplaceSites.Select(site => site.Name).AsEnumerable()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in locationNames)
            {
                if (existingNames.Contains(name))
                {
                    continue;
                }

                db.WorkplaceSites.Add(new WorkplaceSite
                {
                    Name = name,
                    Code = BuildSiteCode(name, null),
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
                existingNames.Add(name);
            }

            if (existingNames.Count == 0)
            {
                db.WorkplaceSites.Add(new WorkplaceSite
                {
                    Name = "الموقع الرئيسي",
                    Code = "MAIN",
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
            }
        }

        private static List<PresenceItemViewModel> BuildPresenceItems(ApplicationDbContext db)
        {
            var permitPresence = db.Permits.AsNoTracking()
                .Where(permit => permit.ArchivedAt == null && permit.CurrentState == "Inside")
                .Select(permit => new PresenceItemViewModel
                {
                    Reference = permit.PermitNumber,
                    FullName = permit.DriverName,
                    PersonType = permit.PermitType == Permit.PermitTypePermanent
                        ? PersonTypes.Employee
                        : PersonTypes.Contractor,
                    PersonTypeDisplay = permit.PermitType == Permit.PermitTypePermanent
                        ? "موظف"
                        : "متعاقد",
                    Location = permit.VisitLocation,
                    Destination = FirstNotEmpty(permit.EmployeeDepartment, permit.DepartmentName),
                    EnteredAt = permit.Activities.OrderByDescending(activity => activity.OccurredAt)
                        .Select(activity => (DateTime?)activity.OccurredAt).FirstOrDefault(),
                    Source = "تصريح",
                })
                .ToList();

            var visitPresence = db.Visits.AsNoTracking()
                .Where(visit => visit.ArchivedAt == null && visit.Status == "Inside" && visit.ExitTime == null)
                .Select(visit => new PresenceItemViewModel
                {
                    Reference = visit.VisitId,
                    FullName = visit.VisitorName,
                    PersonType = PersonTypes.Visitor,
                    PersonTypeDisplay = "زائر",
                    Location = visit.VisitLocation,
                    Destination = visit.VisitedPersonName,
                    EnteredAt = visit.EntryTime,
                    Source = "زيارة",
                })
                .ToList();

            var presence = permitPresence.Concat(visitPresence).ToList();
            if (presence.Count == 0)
            {
                return presence;
            }

            var references = presence.Select(item => item.Reference).Distinct().ToList();
            var profiles = db.PersonProfiles.AsNoTracking()
                .Where(profile => references.Contains(profile.LastReference))
                .Select(profile => new { profile.Id, profile.LastReference })
                .ToList();
            var profileIds = profiles.Select(profile => profile.Id).ToList();
            var photoProfileIds = db.PersonPhotos.AsNoTracking()
                .Where(photo => profileIds.Contains(photo.PersonProfileId))
                .Select(photo => photo.PersonProfileId)
                .ToHashSet();
            var profileByReference = profiles.GroupBy(profile => profile.LastReference)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var item in presence)
            {
                if (profileByReference.TryGetValue(item.Reference, out var profile))
                {
                    item.PersonId = profile.Id;
                    item.HasPhoto = photoProfileIds.Contains(profile.Id);
                }
            }

            return presence;
        }

        private static IReadOnlyList<WorkplaceActivityItemViewModel> BuildRecentActivity(
            ApplicationDbContext db
        )
        {
            var permitActivities = db.PermitActivities.AsNoTracking()
                .OrderByDescending(activity => activity.OccurredAt)
                .Take(30)
                .Select(activity => new WorkplaceActivityItemViewModel
                {
                    Reference = activity.PermitNumber,
                    FullName = activity.DriverName,
                    Action = activity.ActionLabel,
                    Location = activity.GateName,
                    OccurredAt = activity.OccurredAt,
                    IsEntry = activity.ActionType == "Entry" || activity.ActionType == "Return",
                })
                .ToList();
            var visitActivities = db.Visits.AsNoTracking()
                .Where(visit => visit.EntryTime != null || visit.ExitTime != null)
                .OrderByDescending(visit => visit.ExitTime ?? visit.EntryTime)
                .Take(20)
                .Select(visit => new WorkplaceActivityItemViewModel
                {
                    Reference = visit.VisitId,
                    FullName = visit.VisitorName,
                    Action = visit.ExitTime != null ? "خروج زائر" : "دخول زائر",
                    Location = visit.VisitLocation,
                    OccurredAt = visit.ExitTime ?? visit.EntryTime ?? visit.VisitDate,
                    IsEntry = visit.ExitTime == null,
                })
                .ToList();

            return permitActivities.Concat(visitActivities)
                .OrderByDescending(item => item.OccurredAt)
                .Take(24)
                .ToList();
        }

        private static IEnumerable<WorkplaceActivityItemViewModel> BuildVisitTimeline(
            IEnumerable<Visit> visits,
            string fullName
        )
        {
            foreach (var visit in visits)
            {
                if (visit.EntryTime.HasValue)
                {
                    yield return new WorkplaceActivityItemViewModel
                    {
                        Reference = visit.VisitId,
                        FullName = fullName,
                        Action = "دخول زائر",
                        Location = visit.VisitLocation,
                        OccurredAt = visit.EntryTime.Value,
                        IsEntry = true,
                    };
                }

                if (visit.ExitTime.HasValue)
                {
                    yield return new WorkplaceActivityItemViewModel
                    {
                        Reference = visit.VisitId,
                        FullName = fullName,
                        Action = "خروج زائر",
                        Location = visit.VisitLocation,
                        OccurredAt = visit.ExitTime.Value,
                        IsEntry = false,
                    };
                }

                if (!visit.EntryTime.HasValue && !visit.ExitTime.HasValue)
                {
                    yield return new WorkplaceActivityItemViewModel
                    {
                        Reference = visit.VisitId,
                        FullName = fullName,
                        Action = visit.ApprovalStatusDisplay,
                        Location = visit.VisitLocation,
                        OccurredAt = visit.RequestedAtUtc ?? visit.VisitDate,
                        IsEntry = false,
                    };
                }
            }
        }

        private static PersonDirectoryItemViewModel MapDirectoryItem(
            PersonProfile profile,
            IReadOnlyDictionary<string, PresenceItemViewModel> presenceByReference,
            bool hasPhoto
        )
        {
            presenceByReference.TryGetValue(profile.LastReference, out var presence);
            return new PersonDirectoryItemViewModel
            {
                Id = profile.Id,
                FullName = profile.FullName,
                PersonType = profile.PersonType,
                PersonTypeDisplay = PersonTypes.DisplayName(profile.PersonType),
                Reference = profile.LastReference,
                NationalIdMasked = PersonalDataSanitizer.IsInternalReference(profile.NationalId)
                    ? string.Empty
                    : MaskIdentifier(profile.NationalId),
                PhoneNumberMasked = MaskPhone(profile.PhoneNumber),
                Department = profile.Department,
                JobTitle = profile.JobTitle,
                Location = presence?.Location ?? string.Empty,
                IsOnSite = presence != null,
                HasPhoto = hasPhoto,
                LastActivityAt = presence?.EnteredAt ?? profile.LastSeenAtUtc,
            };
        }

        private static void UpsertProfile(
            ApplicationDbContext db,
            IDictionary<string, PersonProfile> existing,
            string sourceKey,
            string personType,
            string fullName,
            string nationalId,
            string phoneNumber,
            string email,
            string employeeNumber,
            string department,
            string jobTitle,
            string organization,
            string lastReference,
            bool isActive,
            DateTime? lastSeenAt,
            DateTime now
        )
        {
            if (!existing.TryGetValue(sourceKey, out var profile))
            {
                profile = new PersonProfile { SourceKey = sourceKey, CreatedAtUtc = now };
                existing[sourceKey] = profile;
                db.PersonProfiles.Add(profile);
            }

            profile.PersonType = personType;
            profile.FullName = FirstNotEmpty(fullName, profile.FullName, "غير مسمى");
            profile.NationalId = FirstNotEmpty(nationalId, profile.NationalId);
            profile.PhoneNumber = FirstNotEmpty(phoneNumber, profile.PhoneNumber);
            profile.Email = FirstNotEmpty(email, profile.Email);
            profile.EmployeeNumber = FirstNotEmpty(employeeNumber, profile.EmployeeNumber);
            profile.Department = FirstNotEmpty(department, profile.Department);
            profile.JobTitle = FirstNotEmpty(jobTitle, profile.JobTitle);
            profile.Organization = FirstNotEmpty(organization, profile.Organization);
            profile.LastReference = FirstNotEmpty(lastReference, profile.LastReference);
            profile.IsActive = isActive;
            profile.LastSeenAtUtc = lastSeenAt ?? profile.LastSeenAtUtc;
            profile.UpdatedAtUtc = now;
        }

        private static string BuildEmployeeSourceKey(
            string? employeeNumber,
            string? nationalId,
            string? phoneNumber,
            string fallback
        ) =>
            !string.IsNullOrWhiteSpace(employeeNumber) ? $"employee:number:{NormalizeKey(employeeNumber)}"
            : !string.IsNullOrWhiteSpace(phoneNumber) ? $"employee:phone:{NormalizeKey(phoneNumber)}"
            : !string.IsNullOrWhiteSpace(nationalId)
                && !PersonalDataSanitizer.IsInternalReference(nationalId)
                    ? $"employee:national:{NormalizeKey(nationalId)}"
            : $"employee:source:{NormalizeKey(fallback)}";

        private static string BuildExternalSourceKey(
            string personType,
            string? nationalId,
            string? phoneNumber,
            string fallback
        ) =>
            !string.IsNullOrWhiteSpace(nationalId)
                && !PersonalDataSanitizer.IsInternalReference(nationalId)
                ? $"{personType}:national:{NormalizeKey(nationalId)}"
            : !string.IsNullOrWhiteSpace(phoneNumber) ? $"{personType}:phone:{NormalizeKey(phoneNumber)}"
            : $"{personType}:source:{NormalizeKey(fallback)}";

        private static string NormalizePersonType(string? personType) =>
            personType switch
            {
                PersonTypes.Employee => PersonTypes.Employee,
                PersonTypes.Visitor => PersonTypes.Visitor,
                PersonTypes.Contractor => PersonTypes.Contractor,
                _ => string.Empty,
            };

        private static string NormalizeKey(string value) =>
            new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        private static bool Contains(string? value, string query) =>
            !string.IsNullOrWhiteSpace(value)
            && value.Contains(query, StringComparison.OrdinalIgnoreCase);

        private static string FirstNotEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

        private static string MaskIdentifier(string value) =>
            value.Length < 6 ? value : $"{value[..2]}******{value[^2..]}";

        private static string MaskPhone(string value) =>
            value.Length < 7 ? value : $"{value[..3]}****{value[^3..]}";

        private static string MaskEmail(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.Contains('@'))
            {
                return string.Empty;
            }

            var parts = value.Split('@', 2);
            var visible = parts[0].Length <= 2 ? parts[0][..1] : parts[0][..2];
            return $"{visible}***@{parts[1]}";
        }

        private static bool IsDeviceOnline(DisplayDevice device, DateTime now) =>
            device.Status == DisplayDeviceStatuses.Approved
            && device.LastSeenUtc.HasValue
            && now - device.LastSeenUtc.Value <= TimeSpan.FromMinutes(2);

        private static string GetDeviceStatusDisplay(DisplayDevice device, DateTime now) =>
            device.Status switch
            {
                DisplayDeviceStatuses.Pending => "بانتظار الموافقة",
                DisplayDeviceStatuses.Disabled => "معطل",
                DisplayDeviceStatuses.Rejected => "مرفوض",
                DisplayDeviceStatuses.Approved when IsDeviceOnline(device, now) => "متصل",
                DisplayDeviceStatuses.Approved => "غير متصل",
                _ => device.Status,
            };

        private static string BuildEntranceCode(string name)
        {
            var ascii = new string(name.Where(char.IsLetterOrDigit).Take(8).ToArray());
            if (string.IsNullOrWhiteSpace(ascii) || ascii.Any(character => character > 127))
            {
                ascii = $"GATE-{Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(name)) % 10000:0000}";
            }

            return ascii.ToUpperInvariant();
        }

        private static string BuildSiteCode(string name, int? id)
        {
            var ascii = new string(name.Where(char.IsLetterOrDigit).Take(8).ToArray());
            if (string.IsNullOrWhiteSpace(ascii) || ascii.Any(character => character > 127))
            {
                ascii = $"SITE-{Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(name)) % 10000:0000}";
            }

            return id.HasValue ? $"{ascii.ToUpperInvariant()}-{id.Value}" : ascii.ToUpperInvariant();
        }
    }
}
