class Paged<T> {
  final List<T> items;
  final int page, pageSize, totalCount, totalPages;
  Paged(this.items, this.page, this.pageSize, this.totalCount, this.totalPages);
  factory Paged.from(Map<String, dynamic> j, T Function(Map<String, dynamic>) map) => Paged(
        ((j['items'] ?? []) as List).map((e) => map(e as Map<String, dynamic>)).toList(),
        j['page'] ?? 1,
        j['pageSize'] ?? 50,
        j['totalCount'] ?? 0,
        j['totalPages'] ?? 0,
      );
}

class User {
  final int id;
  final String email, displayName, role;
  final bool isActive;
  final String? resellerCode;
  User({required this.id, required this.email, required this.displayName, required this.role, required this.isActive, this.resellerCode});
  factory User.fromJson(Map<String, dynamic> j) => User(
        id: j['id'], email: j['email'] ?? '', displayName: j['displayName'] ?? '',
        role: j['role'] ?? 'User', isActive: j['isActive'] ?? true, resellerCode: j['resellerCode']);
}

class AuthResult {
  final String accessToken, refreshToken;
  final int expiresIn;
  final User user;
  AuthResult(this.accessToken, this.refreshToken, this.expiresIn, this.user);
  factory AuthResult.fromJson(Map<String, dynamic> j) =>
      AuthResult(j['accessToken'], j['refreshToken'], j['expiresIn'] ?? 900, User.fromJson(j['user']));
}

class DashboardStats {
  final int campaignsTotal, campaignsRunning, campaignsScheduled;
  final int sentToday, deliveredToday, failedToday;
  final int accountsConnected, accountsTotal;
  final int contactsTotal, contactsGroups;
  final int licensesActive, licensesExpiring;
  final int inboxUnread, inboxThreads;
  DashboardStats(this.campaignsTotal, this.campaignsRunning, this.campaignsScheduled, this.sentToday,
      this.deliveredToday, this.failedToday, this.accountsConnected, this.accountsTotal, this.contactsTotal,
      this.contactsGroups, this.licensesActive, this.licensesExpiring, this.inboxUnread, this.inboxThreads);
  factory DashboardStats.fromJson(Map<String, dynamic> j) => DashboardStats(
        j['campaigns']['total'], j['campaigns']['running'], j['campaigns']['scheduled'],
        j['messages']['sentToday'], j['messages']['deliveredToday'], j['messages']['failedToday'],
        j['accounts']['connected'], j['accounts']['total'],
        j['contacts']['total'], j['contacts']['groups'],
        j['licenses']['active'], j['licenses']['expiringSoon'],
        j['inbox']['unread'], j['inbox']['threads'],
      );
}

class Campaign {
  final int id;
  final String name, status, engineType;
  final String? groupName;
  final int totalRecipients, processedCount, failedCount, progressPercent;
  Campaign(this.id, this.name, this.status, this.engineType, this.groupName, this.totalRecipients,
      this.processedCount, this.failedCount, this.progressPercent);
  factory Campaign.fromJson(Map<String, dynamic> j) => Campaign(
        j['id'], j['name'] ?? '', j['status'] ?? '', j['engineType'] ?? '', j['groupName'],
        j['totalRecipients'] ?? 0, j['processedCount'] ?? 0, j['failedCount'] ?? 0, j['progressPercent'] ?? 0);
}

class CampaignProgress {
  final String status;
  final int total, processed, sent, delivered, read, failed;
  CampaignProgress(this.status, this.total, this.processed, this.sent, this.delivered, this.read, this.failed);
  factory CampaignProgress.fromJson(Map<String, dynamic> j) => CampaignProgress(
        j['status'] ?? '', j['total'] ?? 0, j['processed'] ?? 0, j['sent'] ?? 0,
        j['delivered'] ?? 0, j['read'] ?? 0, j['failed'] ?? 0);
}

class InboxThread {
  final int id, accountId, unreadCount;
  final String accountLabel, contactPhoneMasked;
  final String? contactName, preview;
  final bool isBotEnabled;
  InboxThread(this.id, this.accountId, this.unreadCount, this.accountLabel, this.contactPhoneMasked,
      this.contactName, this.preview, this.isBotEnabled);
  factory InboxThread.fromJson(Map<String, dynamic> j) => InboxThread(
        j['id'], j['accountId'], j['unreadCount'] ?? 0, j['accountLabel'] ?? '',
        j['contactPhoneMasked'] ?? '', j['contactName'], j['preview'], j['isBotEnabled'] ?? true);
}

class InboxMessage {
  final int id;
  final String direction, body;
  final String? status, sentAt;
  InboxMessage(this.id, this.direction, this.body, this.status, this.sentAt);
  factory InboxMessage.fromJson(Map<String, dynamic> j) =>
      InboxMessage(j['id'], j['direction'] ?? 'In', j['body'] ?? '', j['status'], j['sentAt']);
}

class ContactGroup {
  final int id, totalContacts, validContacts;
  final String name, source;
  final List<String> tags;
  ContactGroup(this.id, this.totalContacts, this.validContacts, this.name, this.source, this.tags);
  factory ContactGroup.fromJson(Map<String, dynamic> j) => ContactGroup(
        j['id'], j['totalContacts'] ?? 0, j['validContacts'] ?? 0, j['name'] ?? '', j['source'] ?? '',
        ((j['tags'] ?? []) as List).map((e) => e.toString()).toList());
}

class License {
  final int id, maxActivations, activationCount;
  final String key, licenseType, status;
  final String? expiresAt, userEmail;
  License(this.id, this.maxActivations, this.activationCount, this.key, this.licenseType, this.status,
      this.expiresAt, this.userEmail);
  factory License.fromJson(Map<String, dynamic> j) => License(
        j['id'], j['maxActivations'] ?? 1, j['activationCount'] ?? 0, j['key'] ?? '',
        j['licenseType'] ?? '', j['status'] ?? '', j['expiresAt'], j['userEmail']);
}

class WaAccount {
  final int id, dailyLimit, sentToday;
  final String engineType, displayName, status, warmupState;
  final String? phoneNumber;
  WaAccount(this.id, this.dailyLimit, this.sentToday, this.engineType, this.displayName, this.status,
      this.warmupState, this.phoneNumber);
  factory WaAccount.fromJson(Map<String, dynamic> j) => WaAccount(
        j['id'], j['dailyLimit'] ?? 0, j['sentToday'] ?? 0, j['engineType'] ?? '', j['displayName'] ?? '',
        j['status'] ?? '', j['warmupState'] ?? '', j['phoneNumber']);
}
