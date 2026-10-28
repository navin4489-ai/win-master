import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../core/config.dart';
import '../widgets/logo.dart';

class SplashScreen extends ConsumerStatefulWidget {
  const SplashScreen({super.key});
  @override
  ConsumerState<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends ConsumerState<SplashScreen> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1100),
  )..forward();

  late final Animation<double> _scale =
      CurvedAnimation(parent: _c, curve: Curves.easeOutBack);
  late final Animation<double> _fade =
      CurvedAnimation(parent: _c, curve: const Interval(0.3, 1.0, curve: Curves.easeOut));

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: DecoratedBox(
        decoration: const BoxDecoration(
          gradient: LinearGradient(
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
            colors: [Color(0xFF0B1220), AppTheme.primary],
          ),
        ),
        child: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ScaleTransition(
                scale: _scale,
                child: Container(
                  padding: const EdgeInsets.all(18),
                  decoration: BoxDecoration(
                    color: Colors.white,
                    borderRadius: BorderRadius.circular(26),
                    boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.25), blurRadius: 30, offset: const Offset(0, 12))],
                  ),
                  child: const WinMasterLogo(size: 76),
                ),
              ),
              const SizedBox(height: 22),
              FadeTransition(
                opacity: _fade,
                child: Column(
                  children: [
                    RichText(
                      text: const TextSpan(
                        style: TextStyle(fontSize: 28, fontWeight: FontWeight.w800, letterSpacing: -0.5),
                        children: [
                          TextSpan(text: 'Win', style: TextStyle(color: Colors.white)),
                          TextSpan(text: 'Master', style: TextStyle(color: Color(0xFFBDF5D1))),
                        ],
                      ),
                    ),
                    const SizedBox(height: 6),
                    Text('WhatsApp Marketing Suite',
                        style: TextStyle(color: Colors.white.withValues(alpha: 0.75), fontSize: 13, letterSpacing: 0.3)),
                  ],
                ),
              ),
              const SizedBox(height: 36),
              const SizedBox(
                width: 26,
                height: 26,
                child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2.5),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
