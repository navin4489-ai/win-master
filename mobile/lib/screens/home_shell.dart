import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../data/providers.dart';
import 'dashboard_screen.dart';
import 'campaigns_screen.dart';
import 'inbox_screen.dart';
import 'contacts_screen.dart';

class HomeShell extends ConsumerStatefulWidget {
  const HomeShell({super.key});
  @override
  ConsumerState<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends ConsumerState<HomeShell> {
  int _index = 0;

  static const _titles = ['Dashboard', 'Campaigns', 'Inbox', 'Contacts', 'More'];
  final _pages = const [
    DashboardScreen(),
    CampaignsScreen(),
    InboxScreen(),
    ContactsScreen(),
    MoreScreen(),
  ];

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(_titles[_index])),
      body: IndexedStack(index: _index, children: _pages),
      floatingActionButton: _index == 1
          ? FloatingActionButton.extended(
              onPressed: () => context.push('/campaigns/new'),
              icon: const Icon(Icons.add),
              label: const Text('New Campaign'),
            )
          : null,
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        onDestinationSelected: (i) => setState(() => _index = i),
        destinations: const [
          NavigationDestination(icon: Icon(Icons.dashboard_outlined), selectedIcon: Icon(Icons.dashboard), label: 'Home'),
          NavigationDestination(icon: Icon(Icons.rocket_launch_outlined), selectedIcon: Icon(Icons.rocket_launch), label: 'Campaigns'),
          NavigationDestination(icon: Icon(Icons.chat_outlined), selectedIcon: Icon(Icons.chat), label: 'Inbox'),
          NavigationDestination(icon: Icon(Icons.people_outline), selectedIcon: Icon(Icons.people), label: 'Contacts'),
          NavigationDestination(icon: Icon(Icons.menu), label: 'More'),
        ],
      ),
    );
  }
}

class MoreScreen extends ConsumerWidget {
  const MoreScreen({super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.read(authProvider.notifier);
    final items = <Widget>[
      _section('Messaging'),
      _tile(context, Icons.description_outlined, 'Templates', '/templates'),
      _tile(context, Icons.smart_toy_outlined, 'Auto-Reply Rules', '/rules'),
      _tile(context, Icons.auto_awesome, 'AI Chatbot & Generator', '/ai'),
      _section('Infrastructure'),
      _tile(context, Icons.smartphone, 'WhatsApp Accounts', '/accounts'),
      _tile(context, Icons.router_outlined, 'Proxies', '/proxies'),
      _section('Insights'),
      _tile(context, Icons.bar_chart, 'Reports', '/reports'),
      if (auth.atLeast('Reseller')) ...[
        _section('Administration'),
        _tile(context, Icons.vpn_key, 'Licenses', '/licenses'),
      ],
      if (auth.atLeast('Admin')) _tile(context, Icons.group_outlined, 'Users', '/users'),
      _tile(context, Icons.settings, 'Settings', '/settings'),
    ];
    return ListView(padding: const EdgeInsets.all(12), children: items);
  }

  Widget _section(String label) => Padding(
        padding: const EdgeInsets.fromLTRB(8, 14, 8, 6),
        child: Text(label.toUpperCase(),
            style: const TextStyle(fontSize: 11, fontWeight: FontWeight.w700, color: Color(0xFF6B7A90), letterSpacing: .6)),
      );

  Widget _tile(BuildContext context, IconData icon, String label, String route) => Card(
        margin: const EdgeInsets.only(bottom: 8),
        child: ListTile(
          leading: Icon(icon, color: const Color(0xFF128C4A)),
          title: Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
          trailing: const Icon(Icons.chevron_right),
          onTap: () => context.push(route),
        ),
      );
}
