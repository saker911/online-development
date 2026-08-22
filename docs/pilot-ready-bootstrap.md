# تهيئة مالك المنصة في الإنتاج

صفحة `/Account/InitialSetup` متاحة في بيئة التطوير فقط. في الإنتاج تُرجع الصفحة
`404`، ويُنشأ المالك الأول مرة واحدة من الخادم.

## Docker Compose

أنشئ ملفًا مؤقتًا باسم `.env.bootstrap` على الخادم، واجعل صلاحياته للمالك فقط:

```bash
chmod 600 .env.bootstrap
```

ضع فيه القيم التالية ولا تضفه إلى Git:

```dotenv
Bootstrap__OwnerUsername=owner.main
Bootstrap__OwnerFullName=اسم مالك المنصة
Bootstrap__OwnerPhone=0500000000
Bootstrap__OwnerJobTitle=مالك النظام
Bootstrap__OwnerPassword=CHANGE_ME_STRONG_PASSWORD
Bootstrap__OrganizationName=اسم المنشأة
Bootstrap__OrganizationPhone=0110000000
Bootstrap__OrganizationEmail=admin@example.com
Bootstrap__OrganizationAddress=الرياض
```

نفّذ أمر التهيئة مرة واحدة:

```bash
docker compose -f compose.production.yml run --rm \
  --env-file .env.bootstrap \
  app --bootstrap-owner
```

بعد نجاح الأمر، احذف الملف المؤقت بأداة حذف آمن مناسبة للخادم وتحقق أن تسجيل
الدخول يعمل. إعادة تنفيذ الأمر بعد اكتمال التهيئة لا تنشئ حسابًا إضافيًا.

## تشغيل مباشر دون Docker

صدّر المتغيرات نفسها داخل جلسة طرفية مؤقتة ثم نفّذ:

```bash
dotnet VehiclePermitSystemWeb.dll --bootstrap-owner
```

لا تضع كلمة المرور في `appsettings.json` أو في مستودع Git أو في أمر ظاهر داخل
سجل الطرفية.
