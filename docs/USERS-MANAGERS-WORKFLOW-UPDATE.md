# تعليمات: مراجعة وتحديث آلية المستخدمين ومدراء الأقسام

الهدف
- توثيق الوضع الحالي لطريقة ربط المستخدمين بمدراء الأقسام.
- اقتراح نهج واضح وآمن لتحديث النموذج (UI) وبيانات النموذج (DB) والمنطق (Controllers/Services) مع خطوات تنفيذ قابلة للتنفيذ والاختبار.

1) الملخص الحالي
- الجداول/النماذج ذات العلاقة:
  - `Department` يحتوي حقل `ManagerUsername` (مخزن اسم مستخدم المدير إن وُجد).
  - `UserAccount` يحتوي `Department` لكن لا يحوي `ManagerUsername` (لا يوجد ارتباط صريح لحساب المستخدم إلى مدير محدد).
  - عند إنشاء تصريح، الكود يقرأ مدير القسم عبر `ResolveDepartmentManager(department.ManagerUsername)`.
- النتيجة الحالية:
  - الارتباط بين مستخدم و"مديره" يعتمد على بيانات القسم (مركزية). تغيير مدير القسم ينعكس تلقائياً على جميع المستخدمين المنتمين للقسم.

2) المتطلبات الوظيفية المقترحة
- نريد تمكين علاقة صريحة (اختيارية) بين حساب مستخدم وبين مدير محدد (Username) لِمنح مرونة ربط مُدخلين بيانات بمسؤول مختلف عن مدير القسم.
- قواعد السقوط (fallback):
  - إذا كان `UserAccount.ManagerUsername` محدداً، استخدمه كمدير للموافقة/اتصال.
  - وإلا، استخدم `Department.ManagerUsername` إن وُجد.
  - وإلا: اعتبر أن التصريح يتبع الإدارة العامة (fallback إجرائي أو إشعار للمسؤول).

3) خيارات التنفيذ (مقارنة)
- خيار A — سريع/بلا تغيير DB:
  - لا تضيف حقل في `UserAccount`، بل استخدم خيار UI "ارتباط بمدير القسم" الذي سيعتمد على `Department.ManagerUsername` وقت التشغيل.
  - مميزات: لا توجد هجرة DB، سريع.
  - عيوب: لا يمكن ربط المستخدم بمدير مختلف عن مدير القسم.

- خيار B — مرن (مُوصى به): إضافة `ManagerUsername` في `UserAccount`:
  - حقل جديد في `UserAccount` (string, max length 64).
  - UI: في صفحة إنشاء/تعديل المستخدم قدم: حقل `Department`, زر/checkbox "ربط تلقائيًا بمدير القسم"، وحقل اختياري `ManagerUsername` (autocomplete أو dropdown) قابل للتعديل.
  - عند الحفظ: إذا تم اختيار الربط التلقائي املأ `ManagerUsername = Department.ManagerUsername`، وإلا اترك/احفظ القيمة المُدخلة.
  - مميزات: مرونة، يمكن ربط كل مستخدم بمدير محدد مستقل عن بيانات القسم.
  - عيوب: يتطلب إضافة عمود DB وهجرة بسيطة.

4) التغييرات التقنية المطلوبة (خيار B)
- Model:
  - `Models/UserAccount.cs` — أضف `public string ManagerUsername { get; set; } = string.Empty;`
- DbContext / Schema:
  - `Data/ApplicationDbContext.cs` — اضبط `modelBuilder.Entity<UserAccount>().Property(x => x.ManagerUsername).HasMaxLength(64);`
  - `Services/Core/DatabaseBootstrapService` — استخدم `EnsureSqliteColumn` لإضافة العمود إن غاب (آمن على قواعد SQLite القديمة):
    - مثال SQL: `ALTER TABLE "UserAccounts" ADD COLUMN "ManagerUsername" TEXT NOT NULL DEFAULT '';`
- Permissions / Roles:
  - (اختياري) أضف دور `DataEntry` في `Security/AppSecurity.cs` وعيّن الصلاحيات الافتراضية في `AppPermissions.ApplyRoleDefaults`.
- Views/UI:
  - `Views/Users/Edit.cshtml` و`Create.cshtml`:
    - أضف حقل `ManagerUsername` (dropdown/autocomplete مع قائمة المستخدمين ذوي أدوار المديرين).
    - أضف زر/checkbox "ربط تلقائيًا بمدير القسم" يملأ الحقل تلقائيًا من `Department.ManagerUsername`.
  - تحديث JavaScript (إذا تستخدم autocomplete) لملء والتحقق.
- Controllers:
  - `UsersController` — أثناء الحفظ تحقق: trim القيم، validate أن `ManagerUsername` إن وُجد هو اسم مستخدم صحيح (اختياري)، واحفظ.
- Business logic:
  - `PermitsController.ResolveDepartmentManager` (أو الوظيفة المعنية): عدّل لتفضيل `UserAccount.ManagerUsername` إن وُجد، ثم `Department.ManagerUsername`، ثم fallback.

5) Backfill بيانات (اختياري)
- بعد إضافة العمود، لتعبئة `ManagerUsername` لمستخدمي النظام الموجودين يمكنك تشغيل SQL:
  - ```sql
    UPDATE "UserAccounts" SET "ManagerUsername" = (
      SELECT COALESCE("ManagerUsername", '') FROM "Departments" d WHERE d.Name = "UserAccounts"."Department"
    ) WHERE "ManagerUsername" = '' OR "ManagerUsername" IS NULL;
    ```
- احرص على أخذ نسخة احتياطية من قاعدة البيانات (`vehicle-permit-system.db.bak`) قبل التشغيل.

6) خطوات الاختبار
- اختبار الإنشاء والتعديل لمستخدم جديد: اختيار department + تفعيل خيار الربط التلقائي + حفظ → تأكد من أن `ManagerUsername` مُملأ.
- اختبار تعديل مدير القسم: تغيّر مدير القسم ثم تحقق أن المستخدمين الذين لم يربطوا يدوياً يتبعون التغيير.
- اختبار قبول/اعتماد تصريح: أنشئ تصريحًا وارفعه لاختبار أن من سيُستدعى للموافقة يُحدد حسب الترتيب (User.ManagerUsername → Department.ManagerUsername → fallback).
- اختبار واجهة المستخدم: تحقق من عرض الباركود وحقول المشغل كما قبل.

7) خطة تنفيذ مقترحة (تقديري)
- مرحلة 1 (سريعة): أضف حقل `ManagerUsername` في `Models/UserAccount` و`ApplicationDbContext`; أضف `EnsureSqliteColumn` في `DatabaseBootstrapService`. (30–60 دقيقة)
- مرحلة 2: أدرج الحقل في `Views/Users/Edit.cshtml` مع زر "ربط بمدير القسم" وامكانية البحث في أسماء المديرين. (45–90 دقيقة)
- مرحلة 3: عدّل `UsersController` للحفظ، وأضف اختبار backfill سكريبت كإجراء إداري في `AdministrationController` إن رغبت. (30–60 دقيقة)
- مرحلة 4: حدّث منطق حل المدير في `PermitsController` وامتحن سيناريوهات الموافقة. (30–60 دقيقة)
- مرحلة 5: اختبار متكامل + deploy dev. (30–60 دقيقة)

8) حالات ملحوظة واعتبارات
- إذا أردت ربط مستخدم بمدير خارجي (ليس مدير قسم) فالحقل المقترح يسمح بذلك.
- حافظ على اعتبار أن `ManagerUsername` قد يكون فارغًا دائمًا — تعامل مع fallback بوضوح.
- لا تنس النسخ الاحتياطي قبل أي تعديل في قاعدة البيانات الحيّة.

9) ملفات ستتأثّر (قائمة مرجعية)
- `Models/UserAccount.cs`
- `Data/ApplicationDbContext.cs`
- `Services/Core/DatabaseBootstrapService.cs`
- `Views/Users/Create.cshtml`, `Views/Users/Edit.cshtml`
- `Controllers/UsersController.cs`
- `Controllers/PermitsController.cs` (ResolveDepartmentManager)
- `Security/AppSecurity.cs` (اختياري لإضافة دور جديد)

10) قرارك المطلوب مني الآن
- أبدأ تنفيذ الخيار B (أضف الحقل، واجهة، منطق حفظ، وEnsureSqliteColumn)؟
- أم تريد تنفيذ أسرع (الخيار A) مؤقتًا ثم ترقية لاحقًا؟

---
ملف التعليمات هذا جاهز للتنفيذ على بيئة التطوير. أخبرني أي خيار تختار (A أو B) لأبدأ بالتعديلات، وسأقوم بتطبيقها خطوة بخطوة مع تحديثات وطلبات مراجعة ونسخ احتياطي قبل أي تغيّر في قاعدة البيانات.