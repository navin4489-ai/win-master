import 'package:flutter/material.dart';

class AppConfig {
  /// API base URL. For a local emulator use 10.0.2.2; for a deployed API use the Render URL.
  static String baseUrl = const String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'https://winmaster.wintechinov.com',
  );
}

class AppTheme {
  static const primary = Color(0xFF128C4A);
  static const accent = Color(0xFF25D366);

  static ThemeData light() {
    final scheme = ColorScheme.fromSeed(
      seedColor: primary,
      primary: primary,
      secondary: accent,
      brightness: Brightness.light,
    );
    return ThemeData(
      colorScheme: scheme,
      useMaterial3: true,
      scaffoldBackgroundColor: const Color(0xFFF6F8FB),
      appBarTheme: const AppBarTheme(
        backgroundColor: Colors.white,
        foregroundColor: Color(0xFF0F1B2D),
        elevation: 0,
        centerTitle: false,
      ),
      cardTheme: CardThemeData(
        elevation: 0,
        color: Colors.white,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(14),
          side: const BorderSide(color: Color(0xFFE6EBF2)),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: Colors.white,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: const BorderSide(color: Color(0xFFD4DCE6)),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: const BorderSide(color: Color(0xFFD4DCE6)),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: primary,
          minimumSize: const Size.fromHeight(48),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        ),
      ),
    );
  }
}

/// Status color helpers shared across screens.
Color statusColor(String s) {
  switch (s) {
    case 'Running':
    case 'Connected':
    case 'Sent':
      return const Color(0xFF2563EB);
    case 'Completed':
    case 'Delivered':
    case 'Read':
    case 'Active':
      return const Color(0xFF16A34A);
    case 'Scheduled':
    case 'Paused':
    case 'QrReady':
    case 'Connecting':
      return const Color(0xFFF59E0B);
    case 'Failed':
    case 'Banned':
    case 'Revoked':
      return const Color(0xFFE11D48);
    default:
      return const Color(0xFF64748B);
  }
}
