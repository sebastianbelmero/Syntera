# Diagram Modul Syntera

Diagram alur detail & Data Flow Diagram (DFD) level-1 per modul — disusun dari **kode terimplementasi + UR (PB-1..PB-9) + FSD (2.1–2.31)**.
Folder ini melengkapi diagram ERD (`../syntera_erd.dbml`, `../syntera_erd_master.dbml`, `../syntera_erd_site.dbml`).

**Cara membuka:** file `.drawio` dibuka via [app.diagrams.net](https://app.diagrams.net) / draw.io Desktop / VS Code ext "Draw.io Integration". Semua node dan panah dapat diedit.

## Struktur folder

```
modules/
├── 00_alur_proses_overview.drawio   ← diagram alur bisnis (versi rapian, penomoran 1-10)
├── 01_autentikasi/ … 26_training/   ← 1 folder per modul
│   ├── <modul>_flow.drawio          ← diagram alur detail (berfase, cabang JIKA, loop)
│   └── <modul>_dfd.drawio           ← DFD level-1: aktor → proses → tabel
└── README.md
```

Penomoran 01–10 mengikuti penomoran modul pada diagram overview user. Modul 11–26 = modul pendukung/engine lintas-modul yang belum tergambar di overview.

## Legenda (berlaku untuk semua diagram generator)

| Bentuk | Makna |
|---|---|
| Ellipse abu-abu | Mulai / Selesai |
| Kotak biru muda (#EFF6FF, garis #3B82F6) | Proses / langkah |
| Kotak putus-putus | Sub-proses / jalur alternatif |
| Rhombus amber (#FEF3C7) | Keputusan (cabang "JIKA") |
| Kotak latar putus-putus besar | Fase (pengelompokan langkah) |
| Stick figure | Aktor manusia (DFD) |
| Cylinder putus-putus biru (#DAE8FC) | Sistem eksternal (Oracle EBS EAM, QMS, AD, printer) — konvensi diagram user |
| Cylinder lavender (#F5F3FF) | **Tabel milik modul** (DFD) — nama tabel persis seperti di ERD DBML |
| Kotak abu multi-baris | Tabel referensi milik modul lain (dibaca) |
| Panah solid / putus-putus | Alur tulis data / baca data |

## Indeks modul

| # | Modul (kode FSD) | Alur | DFD | Tabel utama | Status |
|---|---|---|---|---|---|
| 00 | Alur proses bisnis end-to-end | [overview](00_alur_proses_overview.drawio) | — | — | baseline user |
| 01 | Autentikasi (AUTH 2.2) | [flow](01_autentikasi/autentikasi_flow.drawio) | [dfd](01_autentikasi/autentikasi_dfd.drawio) | users, roles, permissions, user_roles, user_permissions, syn_role_designated_user, login_attempt_log, refresh_tokens, audit_logs, user_sync_history, platform_users, password_history, syn_user_site_access, syn_impersonation_session (15) | **sebagian implementasi** (IAM) |
| 02 | Registrasi Aset (RA 2.4) | [flow](02_registrasi_aset/registrasi_aset_flow.drawio) | [dfd](02_registrasi_aset/registrasi_aset_dfd.drawio) | syn_asset, syn_oracle_sync_log, syn_oracle_dlq (3) | target |
| 03 | Master Data (MD 2.3) | [flow](03_master_data/master_data_flow.drawio) | [dfd](03_master_data/master_data_dfd.drawio) | 15 tabel: syn_sop, m_form_template, m_formula(+version, validation), m_uncertainty_template(+component), m_tolerance, m_conversion_table, syn_calibrator, syn_certificate, syn_media, syn_location, syn_department, syn_manufacturer | target |
| 03a–03j | **MD sub-modul (10)** | [sop](03_master_data/md_sop.drawio) · [form template](03_master_data/md_form_template.drawio) · [formula](03_master_data/md_formula.drawio) · [uncertainty](03_master_data/md_uncertainty.drawio) · [tolerance](03_master_data/md_tolerance.drawio) · [conversion](03_master_data/md_conversion.drawio) · [calibrator](03_master_data/md_calibrator.drawio) · [sertifikat](03_master_data/md_sertifikat.drawio) · [media](03_master_data/md_media.drawio) · [master umum](03_master_data/md_master_umum.drawio) | (satu DFD gabungan di 03) | lihat masing-masing | target |
| 04 | Work Order (WO 2.5) | [flow](04_work_order/work_order_flow.drawio) | [dfd](04_work_order/work_order_dfd.drawio) | syn_forecast_cache, syn_wo_ref, syn_wo_snapshot, syn_wo_calibrator, syn_wo_media, syn_wo_cal_point, syn_wo_assignment, syn_wo_cancellation, syn_oracle_sync_log, syn_oracle_conflict_log, syn_oracle_dlq (11) | target |
| 05 | Kalibrasi (CAL 2.6) | [flow](05_kalibrasi/kalibrasi_flow.drawio) | [dfd](05_kalibrasi/kalibrasi_dfd.drawio) | syn_wo_result, syn_wo_point_result, syn_wo_env, syn_wo_uncertainty, syn_attachment, syn_wo_approval, syn_asset_drift_history, syn_external_calibration, syn_cal_execution_override, syn_wo_pre_start_check (10) | target |
| 06 | Cross-Site (CS 2.13) | [flow](06_cross_site/cross_site_flow.drawio) | [dfd](06_cross_site/cross_site_dfd.drawio) | syn_cross_site_calibrator, syn_cross_site_wo_link, syn_cross_site_logistics, syn_cross_site_logistics_checkpoint, syn_cross_site_calibrator_booking, syn_user_site_access, audit_logs (7 — platform DB) | target |
| 07 | Deviasi (DEV 2.14) | [flow](07_deviasi/deviasi_flow.drawio) | [dfd](07_deviasi/deviasi_dfd.drawio) | syn_wo_deviation_link, syn_deviation_attachment, syn_deviation_investigation, syn_qms_sync_log, syn_wo_cancellation, syn_asset_reactivation, audit_logs (7) | target |
| 08 | Report (REP 2.22) | [flow](08_report/report_flow.drawio) | [dfd](08_report/report_dfd.drawio) | syn_report_template, syn_scheduled_report, syn_report_execution, syn_approval_chain, syn_document, audit_logs (6) | target |
| 09 | Approval — engine (APPR 2.7) | [flow](09_approval/approval_flow.drawio) | [dfd](09_approval/approval_dfd.drawio) | syn_workflow_template, syn_approval_instance, syn_approval_step, syn_approval_delegation, syn_approval_matrix, syn_signature_manifest, syn_approval_chain (7) | target |
| 10 | Label (LBL 2.24) | [flow](10_label/label_flow.drawio) | [dfd](10_label/label_dfd.drawio) | m_label_color_config, m_label_attributes, syn_label, syn_asset_offset_history (4) | target |
| 11 | Snapshot immutable (SNP 2.23) | [flow](11_snapshot/snapshot_flow.drawio) | [dfd](11_snapshot/snapshot_dfd.drawio) | syn_wo_snapshot, syn_wo_calibrator, syn_wo_media, syn_wo_cal_point, syn_wo_env, syn_wo_result, syn_snapshot_verification (7) | target |
| 12 | E-Signature — engine (ESIGN 2.8) | [flow](12_esign/esign_flow.drawio) | [dfd](12_esign/esign_dfd.drawio) | syn_signature_manifest, syn_authority_level, syn_reauth_token, syn_otp_code, syn_signature_attempt_log, syn_record_signoff_lock, audit_logs (7) | target |
| 13 | Audit trail (AUDIT 2.9) | [flow](13_audit_trail/audit_trail_flow.drawio) | [dfd](13_audit_trail/audit_trail_dfd.drawio) | **audit_logs (implementasi)**, syn_audit_mode_config, syn_audit_reason_list, syn_audit_reason_item, syn_audit_verification_run (5) | **sebagian implementasi** |
| 14 | Dokumen (DOC 2.11) | [flow](14_dokumen/dokumen_flow.drawio) | [dfd](14_dokumen/dokumen_dfd.drawio) | syn_document, syn_document_version, syn_retention_policy, syn_document_disposal, audit_logs (5) | target |
| 15 | Nomor dokumen — engine (DNG 2.12) | [flow](15_nomor_dokumen/nomor_dokumen_flow.drawio) | [dfd](15_nomor_dokumen/nomor_dokumen_dfd.drawio) | syn_doc_sequence, syn_doc_number_format, syn_doc_number_log (3) | target |
| 16 | Notifikasi — engine (NOTIF 2.10) | [flow](16_notifikasi/notifikasi_flow.drawio) | [dfd](16_notifikasi/notifikasi_dfd.drawio) | syn_notification_template, syn_notification_log, syn_notification_preference, syn_notification_routing (4) | target |
| 17 | CAPA (2.15) | [flow](17_capa/capa_flow.drawio) | [dfd](17_capa/capa_dfd.drawio) | syn_wo_deviation_capa_link, syn_wo_close_override, syn_qms_sync_log, audit_logs (4) | target |
| 18 | Risk Assessment (RISK 2.16) | [flow](18_risk_assessment/risk_assessment_flow.drawio) | [dfd](18_risk_assessment/risk_assessment_dfd.drawio) | syn_risk_assessment, syn_impacted_product_log, audit_logs (3) | target |
| 19 | Kualifikasi IQ/OQ/PQ (QUAL 2.17) | [flow](19_kualifikasi/kualifikasi_flow.drawio) | [dfd](19_kualifikasi/kualifikasi_dfd.drawio) | syn_qualification_plan, syn_qualification, syn_qualification_template, syn_qualification_checklist_item, syn_qualification_evidence (5) | target |
| 20 | Lifecycle aset (LIFE 2.18) | [flow](20_lifecycle_aset/lifecycle_aset_flow.drawio) | [dfd](20_lifecycle_aset/lifecycle_aset_dfd.drawio) | syn_asset_movement, syn_asset_decommission, syn_asset_reactivation, syn_wo_berita_acara, syn_asset, syn_oracle_sync_log, syn_oracle_dlq (7) | target |
| 21 | Integrasi Oracle EBS EAM (ORA 2.19) | [flow](21_integrasi_oracle_ebs/integrasi_oracle_ebs_flow.drawio) | [dfd](21_integrasi_oracle_ebs/integrasi_oracle_ebs_dfd.drawio) | syn_oracle_config, syn_oracle_sync_log, syn_oracle_conflict_log, syn_oracle_dlq, syn_wo_ref, syn_asset (6) | target |
| 22 | Integrasi QMS (QMS 2.20) | [flow](22_integrasi_qms/integrasi_qms_flow.drawio) | [dfd](22_integrasi_qms/integrasi_qms_dfd.drawio) | syn_qms_config, syn_qms_sync_log, syn_wo_deviation_link, syn_document_version (4) | target |
| 23 | Site configuration (SITE 2.21) | [flow](23_site_configuration/site_configuration_flow.drawio) | [dfd](23_site_configuration/site_configuration_dfd.drawio) | **sites (implementasi)**, site_ldap_domains, site_ldap_configs, site_themes, syn_oracle_config, syn_qms_config, syn_approval_matrix, syn_notification_routing, m_label_attributes, syn_user_site_access, platform_settings (11) | **sebagian implementasi** |
| 24 | Administrasi & keamanan (AS 2.1) | [flow](24_administrasi_keamanan/admin_keamanan_flow.drawio) | [dfd](24_administrasi_keamanan/admin_keamanan_dfd.drawio) | platform_users, users, login_attempt_log, refresh_tokens, user_permissions, user_roles, syn_impersonation_session, audit_logs (8) | **sebagian implementasi** (IAM) |
| 25 | Custom fields (CF 2.25) | [flow](25_custom_fields/custom_fields_flow.drawio) | [dfd](25_custom_fields/custom_fields_dfd.drawio) | syn_custom_field_definition, syn_custom_field_option, syn_custom_field_value, syn_custom_field_permission (4) | target |
| 26 | Training & competency (TRAIN 2.31) | [flow](26_training/training_flow.drawio) | [dfd](26_training/training_dfd.drawio) | syn_competency_matrix, syn_training_record (2) | target |

## Pemetaan ke proses bisnis UR

| UR | Modul terkait |
|---|---|
| PB-1 Pembuatan & Release WO | 02 RA → 03 MD → 04 WO |
| PB-2 Penanganan Deviasi & Cancel WO | 07 DEV → 17 CAPA → 06 CS (kompensasi) |
| PB-3 Kalibrasi Eksternal / Vendor | 05 CAL (jalur external) + 21 ORA (PR vendor) |
| PB-4 Eksekusi Kalibrasi Internal | 05 CAL + 11 SNP + 12 ESIGN |
| PB-5 Pelaporan, Review & Approval | 08 REP + 09 APPR + 10 LBL |
| PB-6 Closure | 04 WO (gate) + 13 AUDIT |
| PB-7 Master Data & SOP | 03 MD + 10 sub-modul |
| PB-8 Konfigurasi Sistem & Administrasi | 23 SITE + 24 AS + 01 AUTH |
| PB-9 Skenario Lintas Lokasi | 06 CS (Borrow / Bring & Self-Cal / Full Outsource) |

## Catatan

- **Modul cross-cutting tanpa diagram terpisah** (spesifikasi kendala, diwujudkan di dalam modul lain):
  - *Electronic Records* (2.30) → hash chain & immutable: modul 11 SNP, 12 ESIGN, 13 AUDIT
  - *Data Privacy* (2.29→2.30 DP) → minimalisasi data & retensi: modul 24 AS, 14 DOC
  - *Data Migration* (2.26) → proses satu kali: import Excel RA/MD + onboarding site (23)
  - *Operating Environment* (2.27) & *Backup & Recovery* (2.28) → infrastruktur per-deployment (lihat SETUP.md)
- **DFD kolom label**: label panah ditempatkan dekat sumber; garis putus-putus = baca, solid = tulis.
- Diagram digenerate oleh skrip (`scripts/` sesi pembuatannya) — koordinat dapat digeser bebas di draw.io; regenerasi hanya dari spesifikasi sumber.
- Relasi lintas-database (platform ↔ site) pada DFD ditandai label "(Master DB)" pada tabel; tabel lain berada di `syntera_[site]`.
