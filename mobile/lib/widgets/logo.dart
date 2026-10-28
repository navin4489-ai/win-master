import 'package:flutter/material.dart';
import '../core/config.dart';

/// Win Master brand mark — a rounded chat-bubble shield with a stylized W peak.
/// Drawn with CustomPaint so it needs no asset files and scales crisply.
class WinMasterLogo extends StatelessWidget {
  final double size;
  final bool onDark;
  const WinMasterLogo({super.key, this.size = 72, this.onDark = false});

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: size,
      height: size,
      child: CustomPaint(painter: _LogoPainter(onDark: onDark)),
    );
  }
}

class _LogoPainter extends CustomPainter {
  final bool onDark;
  _LogoPainter({required this.onDark});

  @override
  void paint(Canvas canvas, Size size) {
    final w = size.width, h = size.height;
    final r = w * 0.28;

    // Bubble background (gradient capsule with a tail).
    final bubble = Path()
      ..addRRect(RRect.fromRectAndRadius(
        Rect.fromLTWH(w * 0.06, h * 0.06, w * 0.88, h * 0.80),
        Radius.circular(r),
      ))
      ..moveTo(w * 0.30, h * 0.80)
      ..lineTo(w * 0.16, h * 0.96)
      ..lineTo(w * 0.42, h * 0.80)
      ..close();

    final bg = Paint()
      ..shader = const LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [AppTheme.accent, AppTheme.primary],
      ).createShader(Rect.fromLTWH(0, 0, w, h));
    canvas.drawPath(bubble, bg);

    // The "W / M" peak stroke.
    final stroke = Paint()
      ..color = Colors.white
      ..style = PaintingStyle.stroke
      ..strokeWidth = w * 0.075
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round;

    final mark = Path()
      ..moveTo(w * 0.27, h * 0.33)
      ..lineTo(w * 0.37, h * 0.60)
      ..lineTo(w * 0.50, h * 0.40)
      ..lineTo(w * 0.63, h * 0.60)
      ..lineTo(w * 0.73, h * 0.33);
    canvas.drawPath(mark, stroke);

    // Dot under the peak.
    canvas.drawCircle(Offset(w * 0.50, h * 0.64), w * 0.035, Paint()..color = Colors.white);
  }

  @override
  bool shouldRepaint(covariant _LogoPainter oldDelegate) => oldDelegate.onDark != onDark;
}

/// Logo + wordmark lockup used on the login screen.
class WinMasterWordmark extends StatelessWidget {
  final double logoSize;
  final Color textColor;
  const WinMasterWordmark({super.key, this.logoSize = 40, this.textColor = const Color(0xFF0F1B2D)});

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        WinMasterLogo(size: logoSize),
        const SizedBox(width: 10),
        RichText(
          text: TextSpan(
            style: TextStyle(fontSize: logoSize * 0.55, fontWeight: FontWeight.w800, letterSpacing: -0.5),
            children: [
              TextSpan(text: 'Win', style: TextStyle(color: textColor)),
              const TextSpan(text: 'Master', style: TextStyle(color: AppTheme.primary)),
            ],
          ),
        ),
      ],
    );
  }
}
