-- Plain application notices commit atomically with the application transition.
-- Reward grants and claims remain exclusively in the inventory/mail service.
CREATE OR REPLACE FUNCTION notify_venue_application_mail() RETURNS trigger AS $$
DECLARE
    venue_label TEXT := CASE WHEN NEW.kind='base' THEN '基地' ELSE '赛事' END;
    notice_title TEXT;
    notice_content TEXT;
    destination TEXT := '/account?application=' || NEW.application_id::text || '#sec-manage-' || CASE WHEN NEW.kind='base' THEN 'base' ELSE 'event' END;
    delivery TEXT;
BEGIN
    IF TG_OP='UPDATE' AND NEW.status IS NOT DISTINCT FROM OLD.status THEN
        RETURN NEW;
    END IF;
    IF NEW.status='pending' THEN
        notice_title := venue_label || '申请已提交';
        notice_content := '已收到您提交的' || venue_label || '申请「' || NEW.name || '」，请等待管理员审核。审核结果会通过邮件通知您。';
    ELSIF NEW.status='approved' THEN
        notice_title := venue_label || '申请已通过';
        notice_content := '您的' || venue_label || '申请「' || NEW.name || '」已通过审核。您已成为该' || venue_label || '的负责人，可前往网站首页salasasa.cn的账户面板中继续设置。';
        IF NEW.event_id IS NOT NULL THEN
            destination := '/account?manage=' || NEW.event_id || '#sec-manage-' || CASE WHEN NEW.kind='base' THEN 'base' ELSE 'event' END;
        END IF;
        IF COALESCE(btrim(NEW.review_note),'')<>'' THEN
            notice_content := notice_content || E'\n\n审核备注：\n' || left(NEW.review_note,4000);
        END IF;
    ELSIF NEW.status='rejected' THEN
        notice_title := venue_label || '申请已打回';
        notice_content := '您的' || venue_label || '申请「' || NEW.name || '」需要修改后重新提交。'
            || E'\n\n打回原因：\n' || COALESCE(NULLIF(btrim(NEW.review_note),''),'请查看申请中的审核备注。')
            || E'\n\n请在账户面板的「赛事与基地」中修改并重新提交。';
    ELSE
        RETURN NEW;
    END IF;
    notice_content := translate(left(notice_content,18000), '<>', '＜＞')
        || E'\n\n申请编号：' || NEW.application_id::text;
    delivery := 'venue-' || NEW.application_id::text || '-' || NEW.status || '-' || txid_current()::text;
    INSERT INTO player_mail(user_id,sender,title,content,created_by,delivery_key,delivery_hash,action_path)
    VALUES(NEW.applicant_user_id,'Salasasa 平台审核',notice_title,notice_content,
           COALESCE(NEW.reviewer_user_id,NEW.applicant_user_id),delivery,md5(delivery || notice_content),destination)
    ON CONFLICT (delivery_key) DO NOTHING;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid='event_applications'::regclass
                   AND tgname='venue_application_mail_notice') THEN
        CREATE TRIGGER venue_application_mail_notice AFTER INSERT OR UPDATE OF status ON event_applications
            FOR EACH ROW EXECUTE FUNCTION notify_venue_application_mail();
    END IF;
END $$;
