# فهرس سياسة العقوبات والتصعيد

آخر تحديث: `2026-04-21`

هذه الوثيقة هي المرجع المعتمد والسريع لمعرفة:
- آخر تحديثات سياسة العقوبات
- الحالة الحالية المعتمدة
- ملفات الكود والاختبارات المرتبطة بها
- مكان السجل الزمني الرسمي بعد ترتيب التوثيق وحذف النسخ المكررة

## 1) الحالة الحالية المعتمدة

- `FullAccess` / `خروج ودخول كامل`:
  - حركة حرة دخولًا وخروجًا.
  - تسجيل فقط بدون عقوبات أو تصعيدات.
  - القراءات السلبية أو المزامنة لا تُنشئ `warning` أو `review` أو `stop`.

- `EntryOnly` / `دخول فقط`:
  - هو مسار العقوبة الوحيد للموظفين أثناء ساعات العمل الرسمية.
  - محاولة الخروج غير المصرح بها تُمنع مباشرة.
  - التصعيد يعتمد على أيام مخالفة مميزة، وليس على عدد المسحات المتكررة في اليوم نفسه.

- `ForgotToCheckout`:
  - إذا بقيت جلسة اليوم السابق مفتوحة بدون مخالفة معلقة حقيقية، تُغلق تلقائيًا في اليوم التالي بدون عقوبة.
  - إذا كانت هناك `PendingUnauthorizedExit` فعلية، فلا يتم مسحها بالإغلاق التلقائي، وتستمر إلى المراجعة أو الإيقاف حسب السياسة.

- `Read-only safety`:
  - عرض التصريح، التزامن، وتحديثات القراءة فقط لا تصنع `warning` ولا `review` ولا `stop`.

## 2) السجل الزمني المختصر

### 2026-04-21

- تثبيت السياسة النهائية على `FullAccess` مقابل `EntryOnly`.
- تثبيت سلوك `ForgotToCheckout` بحيث لا يمسح المخالفات المعلقة الحقيقية.
- تثبيت قاعدة `distinct violation days` بدل تكرار نفس اليوم.
- تأكيد أن مسارات القراءة فقط لا تولد عقوبات.
- إعادة تنظيم `PermitBehaviorChecks` والتحقق النهائي من نجاح الحزمة بالكامل.
- ترتيب التوثيق وحذف النسخة المكررة من سجل المراجعة داخل `docs/review/`.

### قبل 2026-04-21

- تم تنفيذ مرحلتي delayed unauthorized exit:
  - `PendingUnauthorizedExit`
  - `UnauthorizedExitNeedsReview`
  - `AdministrativeReviewDismissed`
  - `AdministrativeReviewConfirmed`
  - `UnauthorizedExitStopped`
- أضيفت صلاحية `ReviewUnauthorizedExit` كصلاحية مستقلة للمراجعة الإدارية.
- أضيفت بيانات audit موسعة وربط زمني عبر `SequenceId` داخل النشاطات.

## 3) التوثيق المعتمد بعد الترتيب

- [docs/CHANGELOG.md](./CHANGELOG.md)
  - سجل التغييرات المعتمد للإصدارات والتحديثات المهمة.

- [docs/delayed_unauthorized_exit_design.md](./delayed_unauthorized_exit_design.md)
  - وثيقة التصميم التفصيلية لتدفق العقوبات والتصعيد والمراجعة.

- [docs/penalty_policy_index.md](./penalty_policy_index.md)
  - هذا الملف: فهرس سريع لمعرفة آخر حالة وأين تبدأ.

## 4) الملفات الأساسية في الكود

- [Services/Permits/PermitMovementService.cs](../Services/Permits/PermitMovementService.cs)
  - المنطق التنفيذي الأساسي للحركة، المنع، الإغلاق التلقائي، والتصعيد.

- [Services/Permits/PermitService.cs](../Services/Permits/PermitService.cs)
  - واجهة الخدمة الرئيسية، ومسارات التطبيع وإعادة الضبط للحالات المرتبطة بالعقوبات.

- [Services/Bootstrap/DatabaseBootstrapService.cs](../Services/Bootstrap/DatabaseBootstrapService.cs)
  - تهيئة الأعمدة والحالات الافتراضية، وتهيئة `AccessMode` وحقول `PendingUnauthorizedExit` والعدادات.

- [Models/Entities/Permit.cs](../Models/Entities/Permit.cs)
  - تعريف `AccessMode` والمساعدات مثل `IsFullAccessPermit` و`IsEntryOnlyPermit`.

## 5) مواضع الاختبارات المرجعية

- [tests/PermitBehaviorChecks/Tests/Movement/MovementScenarios.cs](../tests/PermitBehaviorChecks/Tests/Movement/MovementScenarios.cs)
  - يغطي `FullAccess` و`EntryOnly` والتصعيد حسب الأيام المميزة و`ForgotToCheckout` و`read-only safety` و`pending/review/stop workflow` و`work-end closure`.

- [tests/PermitBehaviorChecks/Tests/Permit/PermitScenarios.cs](../tests/PermitBehaviorChecks/Tests/Permit/PermitScenarios.cs)
  - يغطي التفاعل بين نوع التصريح والسياسة اليومية وقرارات البوابة ومسارات الخروج لمرة واحدة.

- [tests/PermitBehaviorChecks/Tests/Common/ScenarioCatalog.Helpers.cs](../tests/PermitBehaviorChecks/Tests/Common/ScenarioCatalog.Helpers.cs)
  - يحتوي المساعدات المشتركة لتوليد أيام المخالفة المؤكدة وتجهيز السيناريوهات المعقدة.

## 6) الكلمات المفتاحية للبحث السريع

- `FullAccess`
- `EntryOnly`
- `PendingUnauthorizedExit`
- `UnauthorizedExitNeedsReview`
- `UnauthorizedExitStopped`
- `AdministrativeReviewDismissed`
- `AdministrativeReviewConfirmed`
- `ForgotToCheckout`
- `AccessMode`

## 7) نقطة البداية لأي تعديل لاحق

1. ابدأ من [Services/Permits/PermitMovementService.cs](../Services/Permits/PermitMovementService.cs).
2. راجع القواعد البنيوية في [Models/Entities/Permit.cs](../Models/Entities/Permit.cs).
3. تحقق من الانعكاس على التهيئة في [Services/Bootstrap/DatabaseBootstrapService.cs](../Services/Bootstrap/DatabaseBootstrapService.cs).
4. راجع آخر تحديث مرتبط في [docs/CHANGELOG.md](./CHANGELOG.md).
5. شغّل حزمة السلوك في [tests/PermitBehaviorChecks/Program.cs](../tests/PermitBehaviorChecks/Program.cs).
