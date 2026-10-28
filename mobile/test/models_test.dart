import 'package:flutter_test/flutter_test.dart';
import 'package:winmaster_app/data/models.dart';

void main() {
  test('User.fromJson parses fields', () {
    final u = User.fromJson({'id': 1, 'email': 'a@b.com', 'displayName': 'Sam', 'role': 'Admin', 'isActive': true});
    expect(u.id, 1);
    expect(u.role, 'Admin');
    expect(u.displayName, 'Sam');
  });

  test('DashboardStats.fromJson maps nested objects', () {
    final s = DashboardStats.fromJson({
      'campaigns': {'total': 3, 'running': 1, 'scheduled': 1},
      'messages': {'sentToday': 10, 'deliveredToday': 8, 'failedToday': 1},
      'accounts': {'connected': 2, 'total': 2},
      'contacts': {'total': 5050, 'groups': 2},
      'licenses': {'active': 4, 'expiringSoon': 1},
      'inbox': {'unread': 2, 'threads': 5},
    });
    expect(s.campaignsTotal, 3);
    expect(s.contactsTotal, 5050);
    expect(s.accountsConnected, 2);
  });

  test('Paged.from maps items', () {
    final p = Paged.from({
      'items': [
        {'id': 1, 'name': 'Promo', 'status': 'Running', 'engineType': 'Baileys'}
      ],
      'page': 1, 'pageSize': 50, 'totalCount': 1, 'totalPages': 1,
    }, Campaign.fromJson);
    expect(p.items.length, 1);
    expect(p.items.first.name, 'Promo');
    expect(p.totalCount, 1);
  });

  test('Campaign handles missing optional fields', () {
    final c = Campaign.fromJson({'id': 2, 'name': 'X', 'status': 'Draft', 'engineType': 'CloudApi'});
    expect(c.groupName, isNull);
    expect(c.totalRecipients, 0);
  });
}
